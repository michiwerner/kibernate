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
using Microsoft.Extensions.Logging;
using IMiddleware = Kibernate.Middlewares.IMiddleware;

namespace Kibernate.Links;

public class HttpLink : ILink, IRunnable
{
    public string Type => "http";
    
    private ILogger _logger;
    
    private ComponentConfig _config;
    
    public HttpLink(ComponentConfig config, ILogger logger, IMiddleware middleware)
    {
        _config = config;
        _logger = logger;
        var dest = $"http://{_config["serviceName"]}:{_config["servicePort"]}";
        var passOriginal = _config.TryGetValue("passOriginalHostHeader", out var poh) && poh == "true";
        SharedHttpHost.RegisterInstance(int.Parse(_config["listenPort"]), dest, passOriginal, middleware);
    }
    
    public async Task RunAsync()
    {
        await SharedHttpHost.RunAsync();
    }
}