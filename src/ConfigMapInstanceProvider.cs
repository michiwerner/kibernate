/*
   Copyright 2025 Michael Werner

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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Kibernate.Clients;

namespace Kibernate;

internal static class NamespaceUtil
{
    public static string GetCurrentNamespace(ILogger logger)
    {
        var ns = Environment.GetEnvironmentVariable("KIBERNATE_NAMESPACE");
        if (!string.IsNullOrWhiteSpace(ns)) return ns!;
        try
        {
            var path = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read current namespace from serviceaccount file");
        }
        return "default";
    }
}

internal class ConfigMapInstanceProvider
{
    private readonly Kubernetes _client;
    private readonly ILogger _logger;
    private readonly string _namespace;

    private static readonly Regex _namePattern = new(
        @"^(kibernate-config-.*|.*-kibernate-config|.*-kibernate-config-.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ConfigMapInstanceProvider(ILogger logger)
    {
        _logger = logger;
        _client = KubernetesClientProvider.Instance;
        _namespace = NamespaceUtil.GetCurrentNamespace(_logger);
    }

    public async Task<List<(string ConfigMapName, InstanceConfig Instance)>> LoadInstancesAsync(CancellationToken ct = default)
    {
        var list = await _client.CoreV1.ListNamespacedConfigMapAsync(_namespace, cancellationToken: ct);
        var result = new List<(string, InstanceConfig)>();
        foreach (var cm in list.Items)
        {
            var name = cm.Metadata?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(name) || !_namePattern.IsMatch(name)) continue;
            if (TryExtractYaml(cm, out var yaml))
            {
                var inst = Config.CreateInstanceFromYaml(yaml!, defaultName: name);
                if (inst != null)
                {
                    // Ensure required collections
                    inst.Middlewares ??= new List<ComponentConfig>();
                    inst.Extensions ??= new List<ComponentConfig>();
                    result.Add((name, inst));
                }
                else
                {
                    _logger.LogError("ConfigMap {ConfigMap} could not be parsed into a Kibernate instance; skipping", name);
                }
            }
            else
            {
                _logger.LogWarning("ConfigMap {ConfigMap} does not contain YAML data (.yml/.yaml) or any entries; skipping", name);
            }
        }
        return result;
    }

    private static bool TryExtractYaml(V1ConfigMap cm, out string? yaml)
    {
        yaml = null;
        var data = cm.Data;
        if (data == null || data.Count == 0) return false;
        // Prefer keys ending in .yml or .yaml
        foreach (var kv in data)
        {
            if (kv.Key.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                yaml = kv.Value;
                return !string.IsNullOrWhiteSpace(yaml);
            }
        }
        // Fallback: if there is exactly one entry, use it
        if (data.Count == 1)
        {
            yaml = data.First().Value;
            return !string.IsNullOrWhiteSpace(yaml);
        }
        return false;
    }
}
