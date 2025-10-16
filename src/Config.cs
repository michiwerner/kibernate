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
        var config = deserializer.Deserialize<Config>(yamlContent);
        return config;
    }

    // Parses YAML in multi-instance format and returns the first instance.
    public static InstanceConfig? CreateInstanceFromYaml(string yamlContent, string defaultName)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        try
        {
            var cfg = deserializer.Deserialize<Config>(yamlContent);
            var first = cfg?.Instances?.Count > 0 ? cfg.Instances[0] : null;
            if (first != null)
            {
                if (string.IsNullOrWhiteSpace(first.Name)) first.Name = defaultName;
                first.Middlewares ??= new List<ComponentConfig>();
                first.Extensions ??= new List<ComponentConfig>();
            }
            return first;
        }
        catch
        {
            return null;
        }
    }
}

