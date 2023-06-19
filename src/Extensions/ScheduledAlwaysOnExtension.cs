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
using Kibernate.Controllers;
using Microsoft.Extensions.Logging;

namespace Kibernate.Extensions;

public class ScheduledAlwaysOnExtension : IExtension, IRunnable, IActivityIndicator
{
    public string? Type => "scheduledAlwaysOn";
    
    private ComponentConfig _config;
    
    private ILogger _logger;

    private TimeSpan _from;
    
    private TimeSpan _to;
    
    private List<DayOfWeek> _weekdays = new();
    
    public ScheduledAlwaysOnExtension(ComponentConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _from = TimeSpan.Parse(config["fromUTC"]);
        _to = TimeSpan.Parse(config["toUTC"]);
        foreach (var weekday in config["weekdays"].Split(","))
        {
            _weekdays.Add(Enum.Parse<DayOfWeek>(weekday, true));
        }
    }
    public async Task InvokeAsync(ControllerContext context, ExtensionDelegate next)
    {
        if (context.EventType == ControllerEventType.DeactivationRequested)
        {
            if (_weekdays.Contains(DateTime.Now.DayOfWeek)
                && DateTime.Now.TimeOfDay >= _from
                && DateTime.Now.TimeOfDay <= _to)
            {
                return;
            }
        }
        await next(context);
    }

    public async Task RunAsync()
    {
        if (!_config.TryGetValue("autostart", out var autostart) || autostart != "true")
        {
            return;
        }
        while (true)
        {
            if (_weekdays.Contains(DateTime.Now.DayOfWeek)
                && DateTime.Now.TimeOfDay >= _from
                && DateTime.Now.TimeOfDay <= _to)
            {
                _ = Task.Run(() => OnActivity?.Invoke(this));
            }
            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }

    public event ActivityHandlerDelegate? OnActivity;
}