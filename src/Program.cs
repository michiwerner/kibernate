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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kibernate;

public class Program 
{
    public static async Task Main()
    {
        var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger<Program>();

        var useDynamic = !string.Equals(Environment.GetEnvironmentVariable("KIBERNATE_DYNAMIC_CONFIG"), "false", StringComparison.OrdinalIgnoreCase);
        var engines = new List<KibernateEngine>();

        List<InstanceConfig> instancesToStart = new();
        if (useDynamic)
        {
            try
            {
                var provider = new ConfigMapInstanceProvider(logger);
                var cmInstances = await provider.LoadInstancesAsync();
                if (cmInstances.Count > 0)
                {
                    foreach (var (cmName, inst) in cmInstances)
                    {
                        if (inst?.Link == null || !inst.Link.TryGetValue("listenPort", out var portStr) || !int.TryParse(portStr, out var port))
                        {
                            logger.LogError("Instance from ConfigMap {ConfigMap} missing valid link.listenPort; skipping", cmName);
                            continue;
                        }
                        instancesToStart.Add(inst);
                    }

                    // Background monitor (poll) to detect changes and apply for name/IP-based routing
                    _ = MonitorConfigMapsAsync(provider,
                        cmInstances.ToDictionary(x => x.Instance.Name, x => x.Instance, StringComparer.OrdinalIgnoreCase),
                        new HashSet<int>(instancesToStart.Select(i => int.Parse(i.Link["listenPort"]))),
                        logger);
                }
                else
                {
                    logger.LogWarning("No Kibernate ConfigMaps found in namespace; falling back to file-based configuration.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading instances from ConfigMaps; falling back to file-based configuration.");
            }
        }

        if (instancesToStart.Count == 0)
        {
            var config = Config.CreateFromFile("/etc/kibernate/kibernate.yml");
            instancesToStart = config.Instances ?? new List<InstanceConfig>();
        }

        logger.LogInformation($"Starting {instancesToStart.Count} Kibernate instance(s)");
        foreach (var instanceConfig in instancesToStart)
        {
            var instanceLogger = loggerFactory.CreateLogger($"KibernateEngine[{instanceConfig.Name}]");
            logger.LogInformation("Creating Kibernate instance: {InstanceName}", instanceConfig.Name);
            var engine = new KibernateEngine(instanceConfig, instanceLogger);
            engines.Add(engine);
        }
        
        // Run all engines in parallel
        var tasks = engines.Select(engine => engine.RunAsync()).ToArray();
        await Task.WhenAll(tasks);
    }

    private static List<string> ParseList(ComponentConfig cfg, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (cfg.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                var parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>();
                foreach (var p in parts)
                {
                    var v = p.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
                return list;
            }
        }
        return new List<string>();
    }

    private static async Task MonitorConfigMapsAsync(ConfigMapInstanceProvider provider, Dictionary<string, InstanceConfig> knownByName, HashSet<int> initialPorts, ILogger logger)
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                var latest = await provider.LoadInstancesAsync();
                var latestByName = latest.ToDictionary(x => x.Instance.Name, x => x.Instance, StringComparer.OrdinalIgnoreCase);

                // Handle updates for existing instances (same name)
                foreach (var kv in latestByName)
                {
                    var name = kv.Key;
                    var newInst = kv.Value;
                    if (!knownByName.TryGetValue(name, out var oldInst))
                    {
                        // New instance appeared
                        if (newInst.Link != null && newInst.Link.TryGetValue("listenPort", out var lpStr) && int.TryParse(lpStr, out var lp))
                        {
                            if (initialPorts.Contains(lp))
                            {
                                logger.LogWarning("ConfigMap change: new instance {InstanceName} detected using existing port {Port}. Dynamic creation of new instances is not supported yet; please restart the pod to activate.", name, lp);
                            }
                            else
                            {
                                logger.LogWarning("ConfigMap change: new instance {InstanceName} requires opening new port {Port}; restart pod required.", name, lp);
                            }
                        }
                        else
                        {
                            logger.LogWarning("ConfigMap change: new instance {InstanceName} has no valid listenPort; ignoring.", name);
                        }
                        continue;
                    }

                    // Compare listen ports
                    int oldPort = 0, newPort = 0;
                    var oldPortOk = oldInst.Link != null && oldInst.Link.TryGetValue("listenPort", out var oldPortStr) && int.TryParse(oldPortStr, out oldPort);
                    var newPortOk = newInst.Link != null && newInst.Link.TryGetValue("listenPort", out var newPortStr) && int.TryParse(newPortStr, out newPort);
                    if (!oldPortOk || !newPortOk)
                    {
                        logger.LogWarning("ConfigMap change for {InstanceName}: invalid link.listenPort; skipping dynamic update.", name);
                        continue;
                    }
                    if (oldPort != newPort)
                    {
                        logger.LogWarning("ConfigMap change for {InstanceName}: listenPort changed from {Old} to {New}. Restart pod required.", name, oldPort, newPort);
                        continue;
                    }

                    // Safe to update routing dynamically
                    string dest = $"http://{newInst.Link["serviceName"]}:{newInst.Link["servicePort"]}";
                    bool passOriginal = newInst.Link.TryGetValue("passOriginalHostHeader", out var poh) && poh == "true";
                    var hosts = ParseList(newInst.Link, "hosts", "host");
                    var updated = Links.SharedHttpHost.TryUpdateRouting(name, dest, passOriginal, hosts);
                    if (updated)
                    {
                        logger.LogInformation("Applied dynamic routing update for instance {InstanceName}.", name);
                    }
                    else
                    {
                        logger.LogWarning("Could not apply dynamic update for {InstanceName}; instance not registered. Restart pod may be required.", name);
                    }
                }

                // Detect removals
                foreach (var name in knownByName.Keys)
                {
                    if (!latestByName.ContainsKey(name))
                    {
                        logger.LogWarning("ConfigMap change: instance {InstanceName} removed. Restart pod required to fully apply removal.", name);
                    }
                }

                knownByName = latestByName;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while monitoring ConfigMaps");
            }
        }
    }
}