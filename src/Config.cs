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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kibernate;

public class ComponentConfig : Dictionary<string, string>  {}

public class InstanceConfig
{
    public string Name { get; set; }

    public ComponentConfig Link { get; set; }

    public List<ComponentConfig> Middlewares { get; set; }
    
    public List<ComponentConfig> Extensions { get; set; }

    public ComponentConfig Controller { get; set; }
}

public class Config
{
    public string Version { get; set; }

    public List<InstanceConfig> Instances { get; set; }
    
    public static Config CreateFromFile(string path)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var yamlContent = File.ReadAllText(path);
        
        // Check if the config is in the old format (single instance)
        if (yamlContent.Contains("link:") && !yamlContent.Contains("instances:"))
        {
            // Parse as old format and convert to new format
            var oldConfig = deserializer.Deserialize<OldConfig>(yamlContent);
            return new Config
            {
                Version = oldConfig.Version,
                Instances = new List<InstanceConfig>
                {
                    new InstanceConfig
                    {
                        Name = "default",
                        Link = oldConfig.Link,
                        Middlewares = oldConfig.Middlewares,
                        Extensions = oldConfig.Extensions,
                        Controller = oldConfig.Controller
                    }
                }
            };
        }
        
        // Parse as new format
        var config = deserializer.Deserialize<Config>(yamlContent);
        return config;
    }
}

// Keep the old config structure for backward compatibility
internal class OldConfig
{
    public string Version { get; set; }

    public ComponentConfig Link { get; set; }

    public List<ComponentConfig> Middlewares { get; set; }
    
    public List<ComponentConfig> Extensions { get; set; }

    public ComponentConfig Controller { get; set; }
}
