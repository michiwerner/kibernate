/*
   Copyright 2023 Michael Werner

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;
using IMiddleware = Kibernate.Middlewares.IMiddleware;

namespace Kibernate.Links;

internal static class SharedHttpHost
{
    private class InstanceRegistration
    {
        public required string Key { get; init; }
        public required int ListenPort { get; set; }
        public required string DestinationPrefix { get; set; }
        public required bool PassOriginalHost { get; set; }
        public required IMiddleware Middleware { get; init; }
        public List<string> Hosts { get; set; } = new();
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, InstanceRegistration> _instancesByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, List<InstanceRegistration>> _instancesByPort = new();
    private static readonly HashSet<int> _ports = new();

    private static WebApplicationBuilder? _builder;
    private static WebApplication? _app;
    private static Task? _runTask;

    private static readonly SocketsHttpHandler _handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 512,
        UseProxy = false
    };
    private static readonly HttpMessageInvoker _httpClient = new(_handler, disposeHandler: false);
    private static readonly ForwarderRequestConfig _requestConfig = new() { ActivityTimeout = TimeSpan.FromMinutes(10) };
    // Using the static helper rather than the HttpForwarder type to avoid accessibility issues on older YARP APIs

    private sealed class HostHeaderTransformer : HttpTransformer
    {
        private readonly bool _passOriginalHost;
        public HostHeaderTransformer(bool passOriginalHost) => _passOriginalHost = passOriginalHost;
        public override ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            var task = base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            if (_passOriginalHost)
            {
                proxyRequest.Headers.Host = httpContext.Request.Host.Value;
            }
            return task;
        }
    }

    public static IReadOnlyCollection<int> GetOpenPorts()
    {
        lock (_lock)
        {
            return _ports.ToArray();
        }
    }

    public static void RegisterOrUpdateInstance(string key, int listenPort, string destinationPrefix, bool passOriginalHost, IEnumerable<string> hosts, IMiddleware middleware)
    {
        lock (_lock)
        {
            _builder ??= WebApplication.CreateBuilder();
            _builder.Services.AddHttpForwarder();

            if (_ports.Add(listenPort))
            {
                _builder.WebHost.UseKestrel(options =>
                {
                    options.ListenAnyIP(listenPort);
                });
            }

            if (!_instancesByKey.TryGetValue(key, out var reg))
            {
                reg = new InstanceRegistration
                {
                    Key = key,
                    ListenPort = listenPort,
                    DestinationPrefix = destinationPrefix,
                    PassOriginalHost = passOriginalHost,
                    Middleware = middleware,
                    Hosts = hosts?.Select(h => h.Trim().ToLowerInvariant()).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList() ?? new List<string>()
                };
                _instancesByKey[key] = reg;
                if (!_instancesByPort.TryGetValue(listenPort, out var list))
                {
                    list = new List<InstanceRegistration>();
                    _instancesByPort[listenPort] = list;
                }
                list.Add(reg);
            }
            else
            {
                // Update existing registration (only safe if port unchanged at runtime)
                reg.DestinationPrefix = destinationPrefix;
                reg.PassOriginalHost = passOriginalHost;
                reg.Hosts = hosts?.Select(h => h.Trim().ToLowerInvariant()).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList() ?? new List<string>();
            }
        }
    }

    public static bool TryUpdateRouting(string key, string destinationPrefix, bool passOriginalHost, IEnumerable<string> hosts)
    {
        lock (_lock)
        {
            if (!_instancesByKey.TryGetValue(key, out var reg)) return false;
            reg.DestinationPrefix = destinationPrefix;
            reg.PassOriginalHost = passOriginalHost;
            reg.Hosts = hosts?.Select(h => h.Trim().ToLowerInvariant()).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList() ?? new List<string>();
            return true;
        }
    }

    public static bool UnregisterInstance(string key)
    {
        lock (_lock)
        {
            if (!_instancesByKey.TryGetValue(key, out var reg)) return false;
            _instancesByKey.Remove(key);
            if (_instancesByPort.TryGetValue(reg.ListenPort, out var list))
            {
                list.RemoveAll(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            }
            return true;
        }
    }

    public static Task RunAsync()
    {
        lock (_lock)
        {
            if (_runTask != null)
            {
                return _runTask;
            }
            if (_builder == null)
            {
                // nothing registered; create a noop task
                _runTask = Task.CompletedTask;
                return _runTask;
            }

            _app = _builder.Build();
            var httpForwarder = _app.Services.GetRequiredService<Yarp.ReverseProxy.Forwarder.IHttpForwarder>();

            foreach (var port in _ports.OrderBy(p => p))
            {
                var pLocal = port;
                _app.MapWhen(ctx => ctx.Connection.LocalPort == pLocal, branch =>
                {
                    branch.Run(async context =>
                    {
                        InstanceRegistration? selected = null;
                        List<InstanceRegistration>? list;
                        lock (_lock)
                        {
                            _instancesByPort.TryGetValue(pLocal, out list);
                            if (list != null && list.Count > 0)
                            {
                                var hostHeader = context.Request.Headers.Host.ToString();
                                var lowerHost = hostHeader?.ToLowerInvariant();
                                if (!string.IsNullOrWhiteSpace(lowerHost))
                                {
                                    var idx = lowerHost.IndexOf(':');
                                    if (idx > 0) lowerHost = lowerHost.Substring(0, idx);
                                    lowerHost = lowerHost.Trim().TrimEnd('.');
                                }

                                // 1) prefer host header match (works for both HTTP and HTTPS after TLS termination)
                                if (!string.IsNullOrWhiteSpace(lowerHost))
                                {
                                    selected = list.FirstOrDefault(r => r.Hosts.Count > 0 && r.Hosts.Contains(lowerHost));
                                }
                                // 2) fallback: if there is exactly one default (no filters) use it
                                if (selected == null)
                                {
                                    var defaults = list.Where(r => r.Hosts.Count == 0).ToList();
                                    if (defaults.Count == 1)
                                    {
                                        selected = defaults[0];
                                    }
                                }
                                // 3) else first as last resort
                                selected ??= list[0];
                            }
                        }

                        if (selected == null)
                        {
                            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                            await context.Response.WriteAsync("No route configured");
                            return;
                        }

                        // Per-request middleware for selected instance
                        await selected.Middleware.InvokeAsync(context, _ => Task.CompletedTask);

                        var transformer = new HostHeaderTransformer(selected.PassOriginalHost);
                        await httpForwarder.SendAsync(context, selected.DestinationPrefix, _httpClient, _requestConfig, transformer);
                    });
                });
            }

            _runTask = _app.RunAsync();
            return _runTask;
        }
    }
}


