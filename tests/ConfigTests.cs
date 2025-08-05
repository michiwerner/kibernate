using System;
using System.IO;
using System.Linq;
using Xunit;
using Kibernate;

namespace Kibernate.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void Config_CreateFromFile_OldFormat_ConvertsToNewFormat()
        {
            // Arrange
            var oldConfigYaml = @"
version: 0
link:
  type: http
  listenPort: 8080
  servicePort: 8080
  serviceName: testtarget
middlewares:
  - type: noneWaiter
extensions: []
controller:
  type: deployment
  namespace: default
  deployment: testtarget
  idleTimeout: 01:00:00
";
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, oldConfigYaml);

            try
            {
                // Act
                var config = Config.CreateFromFile(tempFile);

                // Assert
                Assert.NotNull(config);
                Assert.Equal("0", config.Version);
                Assert.NotNull(config.Instances);
                Assert.Single(config.Instances);
                
                var instance = config.Instances.First();
                Assert.Equal("default", instance.Name);
                Assert.Equal("http", instance.Link["type"]);
                Assert.Equal("8080", instance.Link["listenPort"]);
                Assert.Equal("testtarget", instance.Link["serviceName"]);
                Assert.Single(instance.Middlewares);
                Assert.Equal("noneWaiter", instance.Middlewares.First()["type"]);
                Assert.Empty(instance.Extensions);
                Assert.Equal("deployment", instance.Controller["type"]);
                Assert.Equal("testtarget", instance.Controller["deployment"]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void Config_CreateFromFile_NewFormat_ParsesCorrectly()
        {
            // Arrange
            var newConfigYaml = @"
version: 0
instances:
  - name: instance1
    link:
      type: http
      listenPort: 8080
      servicePort: 8080
      serviceName: service1
    middlewares: []
    extensions: []
    controller:
      type: deployment
      namespace: default
      deployment: deployment1
      idleTimeout: 01:00:00
  - name: instance2
    link:
      type: http
      listenPort: 8081
      servicePort: 8080
      serviceName: service2
    middlewares: []
    extensions: []
    controller:
      type: deployment
      namespace: default
      deployment: deployment2
      idleTimeout: 00:30:00
  - name: instance3
    link:
      type: http
      listenPort: 8082
      servicePort: 8080
      serviceName: service3
    middlewares: []
    extensions: []
    controller:
      type: deployment
      namespace: default
      deployment: deployment3
      idleTimeout: 00:15:00
";
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, newConfigYaml);

            try
            {
                // Act
                var config = Config.CreateFromFile(tempFile);

                // Assert
                Assert.NotNull(config);
                Assert.Equal("0", config.Version);
                Assert.NotNull(config.Instances);
                Assert.Equal(3, config.Instances.Count);
                
                // Verify instance 1
                var instance1 = config.Instances[0];
                Assert.Equal("instance1", instance1.Name);
                Assert.Equal("8080", instance1.Link["listenPort"]);
                Assert.Equal("service1", instance1.Link["serviceName"]);
                Assert.Equal("deployment1", instance1.Controller["deployment"]);
                
                // Verify instance 2
                var instance2 = config.Instances[1];
                Assert.Equal("instance2", instance2.Name);
                Assert.Equal("8081", instance2.Link["listenPort"]);
                Assert.Equal("service2", instance2.Link["serviceName"]);
                Assert.Equal("deployment2", instance2.Controller["deployment"]);
                
                // Verify instance 3
                var instance3 = config.Instances[2];
                Assert.Equal("instance3", instance3.Name);
                Assert.Equal("8082", instance3.Link["listenPort"]);
                Assert.Equal("service3", instance3.Link["serviceName"]);
                Assert.Equal("deployment3", instance3.Controller["deployment"]);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}