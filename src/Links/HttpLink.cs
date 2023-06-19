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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;
using IMiddleware = Kibernate.Middlewares.IMiddleware;

namespace Kibernate.Links;

public class HttpLink : ILink, IRunnable
{
    public string Type => "http";
    
    private ILogger _logger;
    
    private ComponentConfig _config;
    
    private WebApplication _app;
    
    public HttpLink(ComponentConfig config, ILogger logger, IMiddleware middleware)
    {
        _config = config;
        _logger = logger;
        var routes = new List<RouteConfig>
        {
            new RouteConfig {
                RouteId = "catchall",
                ClusterId = "upstream",
                Match = new RouteMatch
                {
                    Path = "{**catch-all}"
                }
            }
        };
        var clusters = new List<ClusterConfig>
        {
            new ClusterConfig
            {
                ClusterId = "upstream",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    { "default", new DestinationConfig() { Address = $"http://{_config["serviceName"]}:{_config["servicePort"]}" } },
                },
                HttpRequest = new ForwarderRequestConfig() { ActivityTimeout = TimeSpan.FromMinutes(10) }
            }
        };
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.Services.AddSingleton<ILogger>(_logger);
        appBuilder.Services.AddSingleton<IMiddleware>(middleware);
        appBuilder.Services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms(builderContext =>
            {
                if (_config.TryGetValue("passOriginalHostHeader", out var passOriginalHostHeader) && passOriginalHostHeader == "true")
                {
                    builderContext.AddOriginalHost();
                }
            });
        _app = appBuilder.Build();
        _app.UseMiddleware<IMiddleware>();
        _app.MapReverseProxy();
    }
    
    public async Task RunAsync()
    {
        await _app.RunAsync($"http://*:{_config["listenPort"]}");
    }
}