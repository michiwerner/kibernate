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
                    // Deduplicate by listenPort to avoid conflicts
                    var usedPorts = new HashSet<int>();
                    foreach (var (cmName, inst) in cmInstances)
                    {
                        if (inst?.Link == null || !inst.Link.TryGetValue("listenPort", out var portStr) || !int.TryParse(portStr, out var port))
                        {
                            logger.LogError("Instance from ConfigMap {ConfigMap} missing valid link.listenPort; skipping", cmName);
                            continue;
                        }
                        if (!usedPorts.Add(port))
                        {
                            logger.LogError("Duplicate listenPort {Port} detected for instance {InstanceName} (ConfigMap {ConfigMap}); skipping", port, inst.Name, cmName);
                            continue;
                        }
                        instancesToStart.Add(inst);
                    }

                    // Background monitor (poll) to detect changes and log
                    _ = Task.Run(async () =>
                    {
                        var known = new HashSet<string>(cmInstances.Select(i => i.ConfigMapName));
                        while (true)
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(30));
                                var latest = await provider.LoadInstancesAsync();
                                var latestNames = new HashSet<string>(latest.Select(i => i.ConfigMapName));
                                if (!known.SetEquals(latestNames))
                                {
                                    logger.LogWarning("Detected changes in Kibernate ConfigMaps. Restart is required to apply changes with the current build.");
                                    known = latestNames;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Error while monitoring ConfigMaps");
                            }
                        }
                    });
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
}