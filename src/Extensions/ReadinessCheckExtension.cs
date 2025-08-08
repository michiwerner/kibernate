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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kibernate.Controllers;
using Microsoft.Extensions.Logging;

namespace Kibernate.Extensions;

public class ReadinessCheckExtension : IExtension
{
    public string? Type => "readinessCheck";
    
    private ILogger _logger;

    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 64
    };
    private static readonly HttpClient _client = new(_sharedHandler, disposeHandler: false);
    
    private readonly Uri _readinessCheckUri;
    
    public ReadinessCheckExtension(ComponentConfig config, ILogger logger)
    {
        _logger = logger;
        _readinessCheckUri = new Uri(config["url"]);
    }
    
    public async Task InvokeAsync(ControllerContext context, ExtensionDelegate next)
    {
        if (context.EventType == ControllerEventType.StatusChangeRequested
            && context["newStatus"] == ControllerStatus.Ready)
        {
            await _waitForReady();
        }
        await next(context);
    }

    private async Task _waitForReady()
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < TimeSpan.FromMinutes(1))
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _client.GetAsync(_readinessCheckUri, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}