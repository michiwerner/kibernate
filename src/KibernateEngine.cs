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

namespace Kibernate;

public class KibernateEngine : IRunnable
{
    private InstanceConfig _config;
    
    private ILogger _logger;

    private Controllers.IController _controller;

    private Links.ILink _link;

    private Middlewares.MiddlewareCollection _middlewares = new();
    
    private Extensions.ExtensionCollection _extensions = new();

    public KibernateEngine(InstanceConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        var lnkConfig = _config.Link;
        var ctrConfig = _config.Controller;
        var mwConfig = _config.Middlewares;
        var extConfig = _config.Extensions;
        
        foreach (var ext in extConfig)
        {
            switch (ext["type"].ToLower())
            {
                case "readinesscheck":
                    _logger.LogInformation("Adding readiness check extension");
                    _extensions.Add(new Extensions.ReadinessCheckExtension(ext, _logger));
                    break;
                case "companiondeployment":
                    _logger.LogInformation("Adding companion deployment extension");
                    _extensions.Add(new Extensions.CompanionDeploymentExtension(ext, _logger));
                    break;
                case "scheduledalwayson":
                    _logger.LogInformation("Adding scheduled always on extension");
                    var scheduledAlwaysOn = new Extensions.ScheduledAlwaysOnExtension(ext, _logger);
                    _extensions.Add(scheduledAlwaysOn);
                    break;
                default:
                    throw new Exception($"Unsupported extension type: {ext["type"]}");
            }
        }

        switch (ctrConfig["type"])
        {
            case "deployment":
                _logger.LogInformation("Using deployment controller");
                _controller = new Controllers.DeploymentController(ctrConfig, _logger, _extensions);
                break;
            case "none":
                _logger.LogInformation("Using none controller");
                _controller = new Controllers.NoneController(ctrConfig, _logger, _extensions);
                break;
            default:
                throw new Exception($"Unsupported controller type: {ctrConfig["type"]}");
        }

        foreach (var mw in mwConfig)
        {
            switch (mw["type"].ToLower())
            {
                case "activity":
                    _logger.LogInformation("Adding activity middleware");
                    _middlewares.Add(new Middlewares.ActivityMiddleware(mw, _logger));
                    break;
                case "fixedresponse":
                    _logger.LogInformation("Adding fixed response middleware");
                    _middlewares.Add(new Middlewares.FixedResponseMiddleware(mw, _logger, _controller));
                    break;
                case "connectwaiter":
                    _logger.LogInformation("Adding connect waiter middleware");
                    _middlewares.Add(new Middlewares.ConnectWaiterMiddleware(mw, _logger, _controller));
                    break;
                case "loadingwaiter":
                    _logger.LogInformation("Adding loading waiter middleware");
                    _middlewares.Add(new Middlewares.LoadingWaiterMiddleware(mw, _logger, _controller));
                    break;
                case "nonewaiter":
                    _logger.LogInformation("Adding none waiter middleware");
                    _middlewares.Add(new Middlewares.NoneWaiterMiddleware(mw, _logger, _controller));
                    break;
                case "uploadsavscanner":
                    _logger.LogInformation("Adding uploads AV scanner middleware");
                    _middlewares.Add(new Middlewares.UploadsAvScannerMiddleware(mw, _logger));
                    break;
                default:
                    throw new Exception($"Unsupported middleware type: {mw["type"]}");
            }
        }

        if (lnkConfig["type"].ToLower() == "http")
        {
            _logger.LogInformation("Using http link");
            _link = new Links.HttpLink(lnkConfig, _logger, _middlewares);
        }
        else
        {
            throw new Exception($"Unsupported link type: {lnkConfig["type"]}");
        }
        
        foreach (var middleware in _middlewares)
        {
            if (middleware is IActivityIndicator activityIndicator) activityIndicator.OnActivity += _controller.HandleOnActivity;
        }
        
        foreach (var extension in _extensions)
        {
            if (extension is IActivityIndicator activityIndicator) activityIndicator.OnActivity += _controller.HandleOnActivity;
        }
        
    }

    public async Task RunAsync()
    {
        var tasks = new List<Task>();
        if (_controller is IRunnable runnableController) tasks.Add(runnableController.RunAsync());
        if (_link is IRunnable runnableLink) tasks.Add(runnableLink.RunAsync());
        foreach (var extension in _extensions)
        {
            if (extension is IRunnable runnableExtension) tasks.Add(runnableExtension.RunAsync());
        }
        await Task.WhenAll(tasks);
    }
}
