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
        public required int ListenPort { get; init; }
        public required string DestinationPrefix { get; init; }
        public required bool PassOriginalHost { get; init; }
        public required IMiddleware Middleware { get; init; }
    }

    private static readonly object _lock = new();
    private static readonly List<InstanceRegistration> _instances = new();
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

    public static void RegisterInstance(int listenPort, string destinationPrefix, bool passOriginalHost, IMiddleware middleware)
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
            _instances.Add(new InstanceRegistration
            {
                ListenPort = listenPort,
                DestinationPrefix = destinationPrefix,
                PassOriginalHost = passOriginalHost,
                Middleware = middleware
            });
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

            foreach (var instance in _instances.OrderBy(i => i.ListenPort))
            {
                var localPort = instance.ListenPort;
                var destination = instance.DestinationPrefix;
                var transformer = new HostHeaderTransformer(instance.PassOriginalHost);
                var middleware = instance.Middleware;

                _app.MapWhen(ctx => ctx.Connection.LocalPort == localPort, branch =>
                {
                    branch.Use(async (context, next) =>
                    {
                        await middleware.InvokeAsync(context, next);
                    });
                    branch.Run(async context =>
                    {
                        await httpForwarder.SendAsync(context, destination, _httpClient, _requestConfig, transformer);
                    });
                });
            }

            _runTask = _app.RunAsync();
            return _runTask;
        }
    }
}


