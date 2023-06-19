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

using System.IO;
using System.Threading.Tasks;
using Kibernate.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kibernate.Middlewares;

public class LoadingWaiterMiddleware : AbstractMatchingMiddleware
{
    public override string Type => "loadingWaiter";

    private string _contentType;

    private string _content;
    
    // ReSharper disable once InconsistentNaming
    private IController _controller { get; }

    public LoadingWaiterMiddleware(ComponentConfig config, ILogger logger, IController controller) : base(config, logger)
    {
        _controller = controller;
        _contentType = Config["contentType"];
        if (Config.TryGetValue("content", out var value))
        {
            _content = value;
        }
        else if (Config.TryGetValue("contentFile", out value))
        {
            _content = File.ReadAllText(value);
        }
        else
        {
            _content = "Loading...";
        }
    }
    
    public override async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldSkip(context) && _controller.Status != ControllerStatus.Ready)
        {
            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = _contentType;
            await response.WriteAsync(_content);
            return;
        }
        await next(context);
    }
}