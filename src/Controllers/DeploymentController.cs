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
using Kibernate.Extensions;
using Microsoft.Extensions.Logging;

namespace Kibernate.Controllers;

public class DeploymentController : IController, IRunnable
{
    public string Type => "deployment";

    public ControllerStatus Status { get; private set; } = ControllerStatus.Unknown;

    public DateTime LastStatusChange { get; private set; } = DateTime.Now;

    public DateTime LastActivity { get; private set; } = DateTime.Now;
    
    public TimeSpan? IdleTimeout { get; private set; }
    
    private ILogger _logger;
    
    private IExtension _extensions;

    private ComponentConfig _config;

    private Kubernetes _client = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());

    public DeploymentController(ComponentConfig config, ILogger logger, IExtension extensions)
    {
        _config = config;
        _logger = logger;
        _extensions = extensions;
        if (_config.TryGetValue("idleTimeout", out var idleTimeout))
        {
            IdleTimeout = TimeSpan.Parse(idleTimeout);
        }
    }

    public async Task RunAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await UpdateStatusAsync();
            if (!IdleTimeout.HasValue || !(DateTime.Now - LastActivity > IdleTimeout)) continue;
            if (Status == ControllerStatus.Ready || Status == ControllerStatus.Activating)
            {
                _logger.LogInformation("Idle timeout reached, deactivating");
                await DeactivateAsync();
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public async void HandleOnActivity(object? sender)
    {
        LastActivity = DateTime.Now;
        if (Status != ControllerStatus.Ready
            && Status != ControllerStatus.Activating)
        {
            await ActivateAsync();
        }
    }

    public async Task ActivateAsync(bool force = false)
    {
        if (!force)
        {
            _logger.LogInformation("Activation requested");
            await _prepareAsync(ControllerEventType.ActivationRequested);
            return;
        }
        var deploymentScale = await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
        await UpdateStatusAsync(deploymentScale);
        if (Status == ControllerStatus.Ready || Status == ControllerStatus.Activating)
        {
            _logger.LogInformation("Already ready or activating");
            return;
        }
        _logger.LogInformation("Activating");
        
        var deploymentScalePatchJson = @"
        [
         { 
          ""path"": ""/spec/replicas"", 
          ""op"": ""replace"", 
          ""value"": 1
         } 
        ]";
        var deploymentScalePatch = new V1Patch(deploymentScalePatchJson, V1Patch.PatchType.JsonPatch);
        await _client.PatchNamespacedDeploymentScaleAsync(deploymentScalePatch, _config["deployment"], _config["namespace"]);
        await UpdateStatusAsync();
    }

    public async Task DeactivateAsync(bool force = false)
    {
        if (!force)
        {
            _logger.LogInformation("Deactivation requested");
            await _prepareAsync(ControllerEventType.DeactivationRequested);
            return;
        }
        var deploymentScale = await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
        await UpdateStatusAsync(deploymentScale);
        if (Status == ControllerStatus.Deactivating || Status == ControllerStatus.Deactivated)
        {
            _logger.LogInformation("Already deactivating or deactivated");
            return;
        }
        _logger.LogInformation("Deactivating");
        var deploymentScalePatchJson = @"
        [
         { 
          ""path"": ""/spec/replicas"", 
          ""op"": ""replace"", 
          ""value"": 0 
         } 
        ]";
        var deploymentScalePatch = new V1Patch(deploymentScalePatchJson, V1Patch.PatchType.JsonPatch);
        await _client.PatchNamespacedDeploymentScaleAsync(deploymentScalePatch, _config["deployment"], _config["namespace"]);
        await UpdateStatusAsync();
    }
    
    private async Task _prepareAsync(ControllerEventType eventType, ControllerStatus newStatus = ControllerStatus.Unknown)
    {
        var context = new ControllerContext(eventType);
        context["status"] = Status;
        context["newStatus"] = newStatus;
        await _extensions.InvokeAsync(context, ExecuteAsync);
    }

    public async Task ExecuteAsync(ControllerContext context)
    {
        switch (context.EventType)
        {
            case ControllerEventType.ActivationRequested:
                await ActivateAsync(true);
                break;
            case ControllerEventType.DeactivationRequested:
                await DeactivateAsync(true);
                break;
            case ControllerEventType.StatusChangeRequested: 
                SetStatus(context["newStatus"], true);
                break;
        }
    }

    public async Task UpdateStatusAsync(V1Scale? deploymentScale = null)
    {
        if (deploymentScale == null)
        {
            deploymentScale = await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
        }

        if (deploymentScale.Status.Replicas > 0 && (deploymentScale.Spec.Replicas ?? 0) > 0)
        {
            SetStatus(ControllerStatus.Ready);
        }
        else if (deploymentScale.Status.Replicas > 0 && (deploymentScale.Spec.Replicas ?? 0) == 0)
        {
            SetStatus(ControllerStatus.Deactivating);
        }
        else if (deploymentScale.Status.Replicas == 0 && (deploymentScale.Spec.Replicas ?? 0) > 0)
        {
            SetStatus(ControllerStatus.Activating);
        }
        else if (deploymentScale.Status.Replicas == 0 && (deploymentScale.Spec.Replicas ?? 0) == 0)
        {
            SetStatus(ControllerStatus.Deactivated);
        }
        else
        {
            throw new Exception($"unexpected deployment status: Status.Replicas={deploymentScale.Status.Replicas}, Spec.Replicas={deploymentScale.Spec.Replicas}");
        }
    }
    
    public async void SetStatus(ControllerStatus status, bool force = false)
    {
        if (Status == status)
        {
            return;
        }
        
        if (!force)
        {
            await _prepareAsync(ControllerEventType.StatusChangeRequested, status);
            return;
        }
        
        _logger.LogInformation($"Status changed from {Status} to {status}");
        Status = status;
        LastStatusChange = DateTime.Now;
    }
}
