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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kibernate.Middlewares;

public abstract class AbstractMatchingMiddleware : IMiddleware
{
    protected ComponentConfig Config { get; init; }
    protected ILogger Logger { get; init; }
    
    private bool _defaultSkip = false;

    private Regex? _includePathRegex;

    private Regex? _excludePathRegex;

    private Regex? _includeUserAgentRegex;

    private Regex? _excludeUserAgentRegex;
    
    public AbstractMatchingMiddleware(ComponentConfig config, ILogger logger)
    {
        Config = config;
        Logger = logger;
        if (Config.TryGetValue("default", out var value))
        {
            _defaultSkip = value switch
            {
                "include" => false,
                "exclude" => true,
                _ => throw new ArgumentException($"Invalid default value {value}")
            };
        }
        if (Config.TryGetValue("includePathRegex", out value))
        {
            _includePathRegex = new Regex(value);
        }
        if (Config.TryGetValue("excludePathRegex", out value))
        {
            _excludePathRegex = new Regex(value);
        }
        if (Config.TryGetValue("includeUserAgentRegex", out value))
        {
            _includeUserAgentRegex = new Regex(value);
        }
        if (Config.TryGetValue("excludeUserAgentRegex", out value))
        {
            _excludeUserAgentRegex = new Regex(value);
        }
    }
    
    protected bool ShouldSkip(HttpContext context)
    {
        var request = context.Request;
        var userAgent = request.Headers.UserAgent.ToString();

        if (_excludePathRegex != null && _excludePathRegex.IsMatch(request.Path))
        {
            Logger.LogInformation($"Skipping {Type} middleware for path {request.Path}");
            return true;
        }

        if (_excludeUserAgentRegex != null && _excludeUserAgentRegex.IsMatch(userAgent))
        {
            Logger.LogInformation($"Skipping {Type} middleware for user agent {userAgent}");
            return true;
        }

        if (_includePathRegex != null && _includePathRegex.IsMatch(request.Path))
        {
            Logger.LogInformation($"Including {Type} middleware for path {request.Path}");
            return false;
        }
        
        if (_includeUserAgentRegex != null && _includeUserAgentRegex.IsMatch(userAgent))
        {
            Logger.LogInformation($"Including {Type} middleware for user agent {userAgent}");
            return false;
        }
        
        if (_defaultSkip)
        {
            Logger.LogInformation($"Default-skipping {Type} middleware for path {request.Path} and user agent {userAgent}");
        }
        else
        {
            Logger.LogInformation($"Default-including {Type} middleware for path {request.Path} and user agent {userAgent}");
        }
            
        return _defaultSkip;
    }

    public abstract Task InvokeAsync(HttpContext context, RequestDelegate next);
    public abstract string Type { get; }
}