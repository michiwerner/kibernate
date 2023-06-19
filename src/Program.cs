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
using System.Threading.Tasks;

namespace Kibernate;

public class Program 
{
    public static async Task Main()
    {
        var logger = LoggerFactory.Create(builder => {
        builder.AddConsole();
        }).CreateLogger<Program>();    
        var config = Config.CreateFromFile("/etc/kibernate/kibernate.yml");
        var kibernate = new KibernateEngine(config, logger);
        await kibernate.RunAsync();
    }
}