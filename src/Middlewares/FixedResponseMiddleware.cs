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

using System.Threading.Tasks;
using Kibernate.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kibernate.Middlewares;

public class FixedResponseMiddleware : AbstractMatchingMiddleware
{
    public override string Type => "fixedResponse";
    
    private IController _controller;
    
    private bool _alwaysRespond = false;

    public FixedResponseMiddleware(ComponentConfig config, ILogger logger, IController controller) : base(config,
        logger)
    {
        _controller = controller;
        if (Config.TryGetValue("alwaysRespond", out var alwaysRespond))
        {
            _alwaysRespond = bool.Parse(alwaysRespond);
        }
    }
    
    public override async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (ShouldSkip(context))
        {
            await next(context);
        }
        else if (_alwaysRespond || _controller.Status != ControllerStatus.Ready)
        {
            var response = context.Response;
            response.StatusCode = int.Parse(Config["statusCode"]);
            response.ContentType = Config["contentType"];
            await response.WriteAsync(Config["content"]);
        }
        else
        {
            await next(context);
        }
    }
}