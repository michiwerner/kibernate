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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using nClam;

namespace Kibernate.Middlewares;

public class UploadsAvScannerMiddleware : IMiddleware
{
    public string Type { get; } = "uploadsAvScanner";

    private ClamClient _client;
    
    private ComponentConfig _config;
    
    private ILogger _logger;
    
    public UploadsAvScannerMiddleware(ComponentConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _client = new ClamClient(_config["clamavHost"], int.Parse(_config["clamavPort"]));
    }
    
    public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
        var request = context.Request;
        if (request.HasFormContentType)
        {
            request.EnableBuffering();
            var form = await request.ReadFormAsync();
            foreach (var file in form.Files)
            {
                var scanResult = await _client.SendAndScanFileAsync(file.OpenReadStream());
                if (scanResult.Result != ClamScanResults.Clean)
                {
                    _logger.LogWarning($"File {file.FileName} is infected or could not be scanned.");
                    context.Response.StatusCode = 406;
                    await context.Response.WriteAsync("At least one of the files is either infected or could not be scanned.");
                    return;
                }
            }
            request.Body.Position = 0;
        }
        await next(context);
    }
}