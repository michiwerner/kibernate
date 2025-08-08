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
using k8s;
using k8s.Models;
using Kibernate.Controllers;
using Microsoft.Extensions.Logging;
using Kibernate.Clients;

namespace Kibernate.Extensions;

public class CompanionDeploymentExtension : IExtension
{
    public string? Type => "companionDeployment";
    
    private ILogger _logger;
    
    private ComponentConfig _config;
    
    private Guid _currentGeneration = Guid.Empty;
    
    private TimeSpan? _headStart;
    
    private TimeSpan? _delayStart;
    
    private TimeSpan? _headStop;
    
    private TimeSpan? _delayStop;
    
    private Kubernetes _client = KubernetesClientProvider.Instance;
    
    public CompanionDeploymentExtension(ComponentConfig config, ILogger logger)
    {
        _logger = logger;
        _config = config;
        
        if (_config.TryGetValue("headStart", out var headStart))
        {
            _headStart = TimeSpan.Parse(headStart);
        }
        
        if (_config.TryGetValue("delayStart", out var delayStart))
        {
            if (_headStart.HasValue)
            {
                throw new InvalidOperationException("Cannot specify both headStart and delayStart");
            }
            _delayStart = TimeSpan.Parse(delayStart);
        }
        
        if (_config.TryGetValue("headStop", out var headStop))
        {
            _headStop = TimeSpan.Parse(headStop);
        }
        
        if (_config.TryGetValue("delayStop", out var delayStop))
        {
            if (_headStop.HasValue)
            {
                throw new InvalidOperationException("Cannot specify both headStop and delayStop");
            }
            _delayStop = TimeSpan.Parse(delayStop);
        }
    }
    
    public async Task InvokeAsync(ControllerContext context, ExtensionDelegate next)
    {
        switch (context.EventType)
        {
            case ControllerEventType.ActivationRequested:
            {
                _currentGeneration = Guid.NewGuid();
                var deploymentScale = await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
                if ((deploymentScale.Spec.Replicas ?? 0) < 1)
                {
                    _logger.LogInformation("Activating companion deployment {deployment} in namespace {namespace}", _config["deployment"], _config["namespace"]);
                    var deploymentScalePatch = @"
                    [
                     { 
                      ""path"": ""/spec/replicas"", 
                      ""op"": ""replace"", 
                      ""value"": 1 
                     } 
                    ]";
                    if (_delayStart.HasValue)
                    {
                        _ = Task.Run(async () =>
                        {
                            var myGeneration = _currentGeneration.ToString();
                            await Task.Delay(_delayStart.Value);
                            if (myGeneration.Equals(_currentGeneration.ToString()))
                            {
                                await _client.PatchNamespacedDeploymentScaleAsync(new V1Patch(deploymentScalePatch, V1Patch.PatchType.JsonPatch), _config["deployment"], _config["namespace"]);
                            }
                        });
                    }
                    else
                    {
                        await _client.PatchNamespacedDeploymentScaleAsync(new V1Patch(deploymentScalePatch, V1Patch.PatchType.JsonPatch), _config["deployment"], _config["namespace"]);
                        if (_headStart.HasValue)
                        {
                            await Task.Delay(_headStart.Value);
                        }
                    }
                }

                break;
            }
            case ControllerEventType.DeactivationRequested:
            {
                _currentGeneration = Guid.NewGuid();
                var deploymentScale =
                    await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
                if ((deploymentScale.Spec.Replicas ?? 0) > 0)
                {
                    _logger.LogInformation("Deactivating companion deployment {deployment} in namespace {namespace}",
                        _config["deployment"], _config["namespace"]);
                    var deploymentScalePatch = @"
                    [
                     { 
                      ""path"": ""/spec/replicas"", 
                      ""op"": ""replace"", 
                      ""value"": 0 
                     } 
                    ]";
                    if (_delayStop.HasValue)
                    {
                        _ = Task.Run(async () =>
                        {
                            var myGeneration = _currentGeneration.ToString();
                            await Task.Delay(_delayStop.Value);
                            if (myGeneration.Equals(_currentGeneration.ToString()))
                            {
                                await _client.PatchNamespacedDeploymentScaleAsync(new V1Patch(deploymentScalePatch, V1Patch.PatchType.JsonPatch), _config["deployment"], _config["namespace"]);
                            }
                        });
                    }
                    else
                    {
                        await _client.PatchNamespacedDeploymentScaleAsync(new V1Patch(deploymentScalePatch, V1Patch.PatchType.JsonPatch), _config["deployment"], _config["namespace"]);
                        if (_headStop.HasValue)
                        {
                            await Task.Delay(_headStop.Value); 
                        }
                    }
                }

                break;
            }
        }

        await next(context);
    }
}