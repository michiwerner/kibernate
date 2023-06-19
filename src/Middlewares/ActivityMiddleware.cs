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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kibernate.Middlewares;

public class ActivityMiddleware : AbstractMatchingMiddleware, IActivityIndicator
{
    public event ActivityHandlerDelegate? OnActivity;

    public override string Type => "activity";

    public ActivityMiddleware(ComponentConfig config, ILogger logger) : base(config, logger) {}

    public override async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldSkip(context))
        {
            Logger.LogInformation("Activity detected");
            _ = Task.Run(() => OnActivity?.Invoke(this));
        }
        await next(context);
    }
}