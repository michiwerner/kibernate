---
date: 2025-10-14T00:00:00Z
researcher: Claude
git_commit: ae0d27097ce84dd4025820cd5d08f0b05ac9add3
branch: dev
repository: kibernate
topic: "Kibernate Configuration Architecture - Current Implementation"
tags: [research, codebase, configuration, yaml, kubernetes, deployment]
status: complete
last_updated: 2025-10-14
last_updated_by: Claude
---

# Research: Kibernate Configuration Architecture - Current Implementation

**Date**: 2025-10-14T00:00:00Z
**Researcher**: Claude
**Git Commit**: ae0d27097ce84dd4025820cd5d08f0b05ac9add3
**Branch**: dev
**Repository**: kibernate

## Research Question

How does Kibernate currently read configuration from a single config file? Document the existing implementation including:
- Where configuration is loaded from
- How config files are structured
- How Kibernate instances are defined
- What Kubernetes ConfigMap interaction exists (if any)
- How this relates to dynamic configuration needs for Flux deployments

## Summary

Kibernate currently uses a **static, file-based YAML configuration system** loaded once at startup. The configuration is read from a hardcoded path `/etc/kibernate/kibernate.yml`, which is mounted from a Kubernetes ConfigMap via a volume in Helm deployments.

The system supports both **single-instance** (legacy) and **multi-instance** configurations, where each instance represents an independent reverse proxy managing one Kubernetes deployment. Configuration is loaded synchronously at startup using YamlDotNet, parsed into C# objects, and distributed to component instances (Links, Controllers, Middlewares, Extensions).

**Critical Finding**: There is **no dynamic configuration loading, no ConfigMap monitoring, and no namespace watching** in the current implementation. Configuration changes require pod restart (triggered by Helm ConfigMap checksum annotation).

## Detailed Findings

### Configuration Loading Entry Point

**[Program.cs:36](src/Program.cs#L36)** - Main application entry point

The configuration is loaded from a **hardcoded path** with no environment variable override:

```csharp
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
```

**Key characteristics**:
- Fixed path: `/etc/kibernate/kibernate.yml` (no command-line args, no env vars)
- One `KibernateEngine` instance created per config instance
- All engines run in parallel via `Task.WhenAll()`
- No hot-reload mechanism
- No fallback configuration paths

### Configuration File Format and Schema

**[Config.cs:39-76](src/Config.cs#L39-L76)** - Configuration class definitions

#### Data Structure

```csharp
public class Config
{
    public string Version { get; set; }
    public List<InstanceConfig> Instances { get; set; }
}

public class InstanceConfig
{
    public string Name { get; set; }
    public ComponentConfig Link { get; set; }
    public List<ComponentConfig> Middlewares { get; set; }
    public List<ComponentConfig> Extensions { get; set; }
    public ComponentConfig Controller { get; set; }
}

public class ComponentConfig : Dictionary<string, string> {}
```

#### YAML Format (Multi-Instance)

```yaml
version: 0
instances:
  - name: web-app
    link:
      type: http
      listenPort: 8080
      servicePort: 8080
      serviceName: webapp
    middlewares:
      - type: activity
      - type: connectWaiter
    extensions: []
    controller:
      type: deployment
      namespace: default
      deployment: webapp
      idleTimeout: 01:00:00
```

#### Configuration Parsing

**[Config.cs:45-75](src/Config.cs#L45-L75)** - YamlDotNet deserialization

```csharp
public static Config CreateFromFile(string path)
{
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();
    var yamlContent = File.ReadAllText(path);

    // Format detection: old format has "link:" but not "instances:"
    if (yamlContent.Contains("link:") && !yamlContent.Contains("instances:"))
    {
        // Convert old single-instance format to new multi-instance format
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

    var config = deserializer.Deserialize<Config>(yamlContent);
    return config;
}
```

**Features**:
- Uses `UnderscoredNamingConvention` (YAML `service_name` → C# `ServiceName`)
- Backward compatibility: auto-converts old single-instance format
- String-based format detection (searches for "link:" and "instances:")
- Synchronous file read via `File.ReadAllText()`

### Instance Definition and Configuration

**Instance Concept**: Each instance represents an independent reverse proxy managing one Kubernetes deployment.

#### Single Instance Example

**[configs/single-instance-new-format.yml](configs/single-instance-new-format.yml)**

```yaml
version: 0
instances:
  - name: default
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
```

#### Multi-Instance Example

**[configs/multi-instance-example.yml](configs/multi-instance-example.yml)**

```yaml
version: 0
instances:
  - name: web-app
    link:
      type: http
      listenPort: 8080
      servicePort: 8080
      serviceName: webapp
    middlewares:
      - type: activity
      - type: connectWaiter
    extensions: []
    controller:
      type: deployment
      namespace: default
      deployment: webapp
      idleTimeout: 01:00:00

  - name: api-service
    link:
      type: http
      listenPort: 8081
      servicePort: 8080
      serviceName: api
    middlewares:
      - type: activity
      - type: loadingWaiter
    extensions:
      - type: readinessCheck
        url: http://api:8080/health
    controller:
      type: deployment
      namespace: default
      deployment: api
      idleTimeout: 00:30:00

  - name: background-worker
    link:
      type: http
      listenPort: 8082
      servicePort: 8080
      serviceName: worker
    middlewares:
      - type: noneWaiter
    extensions:
      - type: scheduledAlwaysOn
        fromUTC: 09:00:00
        toUTC: 17:00:00
        weekdays: Monday,Tuesday,Wednesday,Thursday,Friday
    controller:
      type: deployment
      namespace: default
      deployment: worker
      idleTimeout: 00:15:00
```

**Key characteristics**:
- Each instance has unique `name` and `listenPort`
- Each instance targets a different Kubernetes `serviceName`
- Instances run independently in parallel within single Kibernate pod
- Each instance can have different middlewares, extensions, and timeouts

### Component Initialization from Configuration

**[KibernateEngine.cs:38-132](src/KibernateEngine.cs#L38-L132)** - Component instantiation

Components are initialized in this order:

1. **Extensions** (lines 47-67) - Type-based switch instantiation
2. **Controller** (lines 69-81) - Receives extensions collection
3. **Middlewares** (lines 83-110) - Some receive controller reference
4. **Link** (lines 112-120) - Receives middleware collection

Example component config access:

```csharp
// Direct indexer (throws if missing)
var dest = $"http://{_config["serviceName"]}:{_config["servicePort"]}";

// TryGetValue pattern (safe for optional)
if (_config.TryGetValue("idleTimeout", out var idleTimeout))
{
    IdleTimeout = TimeSpan.Parse(idleTimeout);
}

// Boolean flag pattern
var passOriginal = _config.TryGetValue("passOriginalHostHeader", out var poh) && poh == "true";
```

### Kubernetes Integration - Current State

**[Clients/KubernetesClientProvider.cs](src/Clients/KubernetesClientProvider.cs)** - K8s client singleton

```csharp
private static readonly Lazy<Kubernetes> _singleton = new(() =>
    new Kubernetes(KubernetesClientConfiguration.InClusterConfig()));
public static Kubernetes Instance => _singleton.Value;
```

**What EXISTS**:
- Singleton Kubernetes client with in-cluster config
- Deployment reading: `ReadNamespacedDeploymentScaleAsync()`
- Deployment scaling: `PatchNamespacedDeploymentScaleAsync()`
- 5-second polling loop for deployment status

**What does NOT exist**:
- ❌ ConfigMap API interaction
- ❌ ConfigMap watching/monitoring
- ❌ Namespace-level resource listing
- ❌ Dynamic configuration reload
- ❌ File system watching on mounted ConfigMap volume
- ❌ Environment variable for namespace detection

**[Controllers/DeploymentController.cs:171](src/Controllers/DeploymentController.cs#L171)** - Kubernetes API usage

```csharp
await _client.ReadNamespacedDeploymentScaleAsync(_config["deployment"], _config["namespace"]);
await _client.PatchNamespacedDeploymentScaleAsync(patch, _config["deployment"], _config["namespace"]);
```

Each controller reads `namespace` and `deployment` from its component config.

### Helm Chart ConfigMap Integration

**[deployments/helm/kibernate-chart/templates/configmap.yaml](deployments/helm/kibernate-chart/templates/configmap.yaml)**

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: {{ include "kibernate.fullname" . }}
  labels:
    {{- include "kibernate.labels" . | nindent 4 }}
data:
  kibernate.yml: |
    {{- toYaml .Values.kibernate | nindent 4 }}
```

**[deployments/helm/kibernate-chart/templates/deployment.yaml:46-67](deployments/helm/kibernate-chart/templates/deployment.yaml#L46-L67)**

```yaml
volumeMounts:
  - name: config
    mountPath: /etc/kibernate
    readOnly: true

volumes:
  - name: config
    configMap:
      name: {{ include "kibernate.fullname" . }}
```

**Configuration flow**:
1. Helm values under `kibernate:` key → ConfigMap data
2. ConfigMap mounted as volume at `/etc/kibernate/`
3. Application reads `/etc/kibernate/kibernate.yml` at startup
4. ConfigMap changes trigger pod restart (via checksum annotation)

### RBAC Permissions

**[deployments/helm/kibernate-chart/templates/role.yaml](deployments/helm/kibernate-chart/templates/role.yaml)**

Current permissions:
- `apiGroups: ["apps"]` `resources: ["deployments"]` `verbs: ["get", "watch"]`
- `apiGroups: ["apps"]` `resources: ["deployments/scale"]` `verbs: ["get", "patch", "update"]`

**Missing permissions for ConfigMap monitoring**:
- ❌ No `apiGroups: [""]` `resources: ["configmaps"]` permissions
- ❌ No namespace-level list/watch permissions

### Configuration Examples in Codebase

**Test configurations** (9 files):
- Single instance: [configs/testing.yml](configs/testing.yml)
- Single instance (new format): [configs/single-instance-new-format.yml](configs/single-instance-new-format.yml)
- Multi-instance (3 instances): [configs/multi-instance-example.yml](configs/multi-instance-example.yml)
- HTTP passthrough test: [configs/tests/helm/01-test-http-passthrough.yml](configs/tests/helm/01-test-http-passthrough.yml)
- HTTP activation test: [configs/tests/helm/02-test-http-activation.yml](configs/tests/helm/02-test-http-activation.yml)
- Companion deployment test: [configs/tests/helm/03-test-companion-deployment-activation.yml](configs/tests/helm/03-test-companion-deployment-activation.yml)
- HTTP deactivation test: [configs/tests/helm/04-test-http-deactivation.yml](configs/tests/helm/04-test-http-deactivation.yml)
- Companion deactivation test: [configs/tests/helm/05-test-companion-deployment-deactivation.yml](configs/tests/helm/05-test-companion-deployment-deactivation.yml)
- Multi-instance test: [configs/tests/helm/06-test-multi-instance.yml](configs/tests/helm/06-test-multi-instance.yml)

**Default Helm values**: [deployments/helm/kibernate-chart/values.yaml:6-21](deployments/helm/kibernate-chart/values.yaml#L6-L21)

## Code References

- [src/Program.cs:36](src/Program.cs#L36) - Configuration loading entry point
- [src/Config.cs:45-75](src/Config.cs#L45-L75) - Config parsing and format detection
- [src/Config.cs:26-37](src/Config.cs#L26-L37) - InstanceConfig schema definition
- [src/KibernateEngine.cs:38-132](src/KibernateEngine.cs#L38-L132) - Component initialization from config
- [src/Clients/KubernetesClientProvider.cs](src/Clients/KubernetesClientProvider.cs) - Kubernetes client singleton
- [src/Controllers/DeploymentController.cs:171](src/Controllers/DeploymentController.cs#L171) - Kubernetes API usage
- [deployments/helm/kibernate-chart/templates/configmap.yaml](deployments/helm/kibernate-chart/templates/configmap.yaml) - Helm ConfigMap template
- [deployments/helm/kibernate-chart/templates/deployment.yaml:46-67](deployments/helm/kibernate-chart/templates/deployment.yaml#L46-L67) - Volume mount configuration
- [deployments/helm/kibernate-chart/templates/role.yaml](deployments/helm/kibernate-chart/templates/role.yaml) - RBAC permissions

## Architecture Documentation

### Current Configuration Flow

```
Helm Values (kibernate:)
    ↓
ConfigMap (kibernate.yml)
    ↓
Volume Mount (/etc/kibernate/)
    ↓
File Read (Config.CreateFromFile)
    ↓
YamlDotNet Deserialization
    ↓
Config Object (instances list)
    ↓
KibernateEngine per Instance
    ↓
Components (Extensions → Controller → Middlewares → Link)
```

### Instance Lifecycle

1. **Startup**: Config loaded from file, all instances created
2. **Runtime**: Each instance runs independently in parallel
3. **Config Change**: Pod restart required (Helm checksum triggers recreate)
4. **No Hot-Reload**: Configuration is immutable after startup

### Component Configuration Pattern

All components use `ComponentConfig` (Dictionary<string, string>):

```csharp
// Required values: direct indexer (throws if missing)
var serviceName = config["serviceName"];

// Optional values: TryGetValue pattern
if (config.TryGetValue("idleTimeout", out var timeout))
    IdleTimeout = TimeSpan.Parse(timeout);

// Boolean flags: TryGetValue + string comparison
var enabled = config.TryGetValue("enabled", out var val) && val == "true";
```

### Kubernetes Interaction Scope

**Current scope**: Limited to deployment scaling
- Reads deployment scale via polling (5-second interval)
- Patches deployment scale to 0 or 1 based on activity

**No interaction with**:
- ConfigMaps (not read via API)
- Namespace-level resources
- Resource watching (uses polling instead)

## Historical Context (from thoughts/)

No thoughts directory was found in this repository. This is the first research document created.

## Related Research

This is the first research document in this repository.

## Open Questions

### For Understanding Current Implementation
1. Why was file-based configuration chosen over ConfigMap watching?
2. Are there performance concerns with the current polling approach?
3. What is the typical number of instances deployed in production?

### For Future Dynamic Configuration
1. How should ConfigMap naming patterns be detected? (prefix, suffix, infix patterns mentioned)
2. Should existing instances be updated or recreated when config changes?
3. How should invalid ConfigMaps be handled (validation, logging)?
4. What RBAC permissions would be needed for ConfigMap monitoring?
5. Should namespace be detected from environment or stay in config?

## Appendix: Configuration Schema Reference

### Top-Level Config
- `version`: string (currently "0")
- `instances`: array of InstanceConfig

### InstanceConfig
- `name`: string (unique identifier for logging)
- `link`: ComponentConfig (HTTP reverse proxy config)
- `middlewares`: array of ComponentConfig (request/response handlers)
- `extensions`: array of ComponentConfig (lifecycle extensions)
- `controller`: ComponentConfig (deployment management)

### Link (type: http)
- `type`: "http"
- `listenPort`: integer (port to listen on)
- `servicePort`: integer (upstream service port)
- `serviceName`: string (Kubernetes service name)
- `passOriginalHostHeader`: "true"|"false" (optional, default false)

### Controller (type: deployment)
- `type`: "deployment"
- `namespace`: string (Kubernetes namespace)
- `deployment`: string (deployment name)
- `idleTimeout`: TimeSpan string (e.g., "01:00:00")

### Middleware Types
- `activity`: Tracks HTTP activity
- `connectWaiter`: Waits for successful connection
- `loadingWaiter`: Shows loading page during activation
- `fixedResponse`: Returns fixed HTTP response
- `noneWaiter`: No-op middleware

### Extension Types
- `readinessCheck`: HTTP readiness probe
  - `url`: string (full URI for health check)
- `companionDeployment`: Manages companion deployment
  - `namespace`: string
  - `deployment`: string
  - `headStart`/`delayStart`: TimeSpan (mutually exclusive)
  - `headStop`/`delayStop`: TimeSpan (mutually exclusive)
- `scheduledAlwaysOn`: Time-based always-on schedule
  - `fromUTC`: TimeSpan (start time)
  - `toUTC`: TimeSpan (end time)
  - `weekdays`: comma-separated DayOfWeek values
  - `autostart`: "true"|"false" (optional)
