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
        
        var config = Config.CreateFromFile("/etc/kibernate/kibernate.yml");
        
        logger.LogInformation($"Starting {config.Instances.Count} Kibernate instance(s)");
        
        var engines = new List<KibernateEngine>();
        foreach (var instanceConfig in config.Instances)
        {
            var instanceLogger = loggerFactory.CreateLogger($"KibernateEngine[{instanceConfig.Name}]");
            logger.LogInformation($"Creating Kibernate instance: {instanceConfig.Name}");
            var engine = new KibernateEngine(instanceConfig, instanceLogger);
            engines.Add(engine);
        }
        
        // Run all engines in parallel
        var tasks = engines.Select(engine => engine.RunAsync()).ToArray();
        await Task.WhenAll(tasks);
    }
}