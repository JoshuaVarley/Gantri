# Shared Dataverse Connection Provider — Implementation Plan

**Author:** Joshua Varley
**Date:** February 28, 2026
**Status:** Draft
**Target Framework:** Gantri (.NET 10)

---

## 1. Problem Statement

Gantri's plugin system is stateless by design. Each plugin action receives an `ActionContext` with parameters but no shared services. The `IPluginServices` interface exists in `Gantri.Plugins.Sdk` and `ActionContext` has a `Services` property, but it is **never populated** — always `null`. Plugins today cannot access framework-level services like logging, configuration, or external connections.

We need to build a D365/Dataverse plugin toolkit where multiple tool plugins (metadata, solutions, security, records, etc.) share a single managed connection pool — without each plugin managing its own authentication and `ServiceClient` lifecycle. As a consultant, you need to configure multiple Dataverse environments (dev, staging, prod, client orgs) and switch between them mid-session, similar to how XrmToolBox manages connections.

The existing JavaScript D365 example (`d365_connector_example/`) demonstrates the capability set we want — metadata inspection, solution management, security auditing, workflow inspection — but it runs as external MCP processes using `dynamics-web-api`. We want native .NET plugins using the official Microsoft Dataverse SDK.

---

## 2. Why Approach 1: Framework-Level Services via IPluginServices

We evaluated three approaches. Approach 1 was selected for the following reasons:

### 2.1 No Plugin Loader Changes Required

Plugins stay isolated in separate `AssemblyLoadContext`s — the `NativePluginLoader` creates a per-plugin `NativePluginContext(isCollectible: true)` for assembly isolation and unloading. The `IPluginServices` interface lives in `Gantri.Plugins.Sdk`, which is loaded in the default ALC and shared with all plugins. No cross-ALC type casting is needed because every plugin sees the same interface type.

### 2.2 Generic Pattern for Any External Service

`GetService<T>()` works for any future external service, not just Dataverse. A future SharePoint, Azure DevOps, or Salesforce connection provider would follow the identical pattern: implement `IConnectionProvider`, register in DI, and plugins resolve it through `context.Services.GetService<T>()`. Build the plumbing once, reuse everywhere.

### 2.3 Minimal Breaking Changes

The implementation touches a small, well-defined set of files:

- One new property on `PluginActionInput` (nullable, backward compatible)
- One new method on `IPluginServices` (additive, no existing callers break)
- Wiring in the Bridge layer to pass services through

Existing plugins are completely unaffected — `Services` defaults to `null` and no existing code calls it.

### 2.4 Official SDK Access

The `ServiceClient` lives inside a framework-level `Gantri.Dataverse` project using `Microsoft.PowerPlatform.Dataverse.Client`. Plugins interact through the `IConnectionProvider` / `IServiceConnection` abstraction in the SDK. This gives us full access to `IOrganizationServiceAsync`, metadata APIs, `FetchExpression`, solution packaging, and all the strongly-typed request/response classes.

### 2.5 Approaches Not Selected

**Approach 2 (Shared ALC for plugin families):** Would allow D365 plugins to directly share `ServiceClient` instances by loading them into the same `AssemblyLoadContext`. Rejected because it requires significant loader changes, reduces isolation (one plugin crash affects the whole family), and introduces manifest-level dependency declarations. Can be added later if raw SDK performance becomes a bottleneck.

**Approach 3 (Hybrid):** Combines Approaches 1 and 2. Unnecessary complexity for the initial implementation. Approach 1 covers 90%+ of D365 operations cleanly.

---

## 3. Architectural Prerequisite: Reference Direction

A critical constraint must be resolved before implementation can begin.

### The Problem

`PluginActionInput` lives in `Gantri.Abstractions`. We need it to reference `IPluginServices`. But today `IPluginServices` lives in `Gantri.Plugins.Sdk`, which **depends on** Abstractions — not the other way around:

```
Gantri.Abstractions          ← zero project references (only M.E.AI.Abstractions NuGet)
    ↑
Gantri.Plugins.Sdk           ← references Abstractions
    ↑
Built-in plugins             ← reference SDK
```

We cannot add a reverse reference from Abstractions → SDK without creating a circular dependency.

### The Solution

Promote `IPluginServices`, `IPluginLogger`, and `PluginLogLevel` from SDK to Abstractions. The SDK continues to reference Abstractions and gains access to the interface transitively. Plugin authors already reference SDK (which references Abstractions), so they see the interface either way. No circular dependency is created.

The SDK retains backward-compatible re-export interfaces that inherit from the Abstractions versions, so existing plugin code that uses `Gantri.Plugins.Sdk.IPluginServices` continues to compile without changes.

---

## 4. Implementation Steps

### Step 1: Create `IPluginServices` in Abstractions

**New file:** `src/core/Gantri.Abstractions/Plugins/IPluginServices.cs`

Define the canonical interfaces here:

```csharp
using Microsoft.Extensions.AI;

namespace Gantri.Abstractions.Plugins;

public interface IPluginServices
{
    IPluginLogger GetLogger(string categoryName);
    string? GetConfig(string key);
    IChatClient? GetChatClient(string? modelAlias = null);
    T? GetService<T>(string? name = null) where T : class;
}

public interface IPluginLogger
{
    void Log(PluginLogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public enum PluginLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}
```

**Why `IPluginLogger` instead of `ILogger`:** Avoids naming collision with `Microsoft.Extensions.Logging.ILogger`, which is heavily used throughout the framework. The rename is internal to the Abstractions namespace; the SDK re-export can keep the original `ILogger` name for backward compatibility.

**Why `GetService<T>` includes an optional `name` parameter:** Supports keyed/named service resolution (available in .NET 8+ via `IKeyedServiceProvider`). This enables scenarios like multiple connection providers of the same type registered under different names.

### Step 2: Update SDK Re-Exports

**Modified file:** `src/core/Gantri.Plugins.Sdk/IPluginServices.cs`

Replace the definitions with interfaces that inherit from the Abstractions versions:

```csharp
using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Sdk;

public interface IPluginServices : Gantri.Abstractions.Plugins.IPluginServices;

public interface ILogger : IPluginLogger;

public enum LogLevel
{
    Debug = PluginLogLevel.Debug,
    Information = PluginLogLevel.Information,
    Warning = PluginLogLevel.Warning,
    Error = PluginLogLevel.Error
}
```

**Impact on existing code:** All 11 built-in plugins use `ISdkPluginAction`, `ActionContext`, and `ActionResult` via implicit usings. None explicitly reference `IPluginServices`, `ILogger`, or `LogLevel`. The `ReflectionPluginActionAdapter` imports `Gantri.Plugins.Sdk` but only uses the action/context types. No code changes are needed in any existing plugin.

### Step 3: Add `Services` to `PluginActionInput`

**Modified file:** `src/core/Gantri.Abstractions/Plugins/IPlugin.cs`

Now that `IPluginServices` lives in Abstractions (same project), add it directly:

```csharp
public sealed class PluginActionInput
{
    public string ActionName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public string? WorkingDirectory { get; init; }
    public IPluginServices? Services { get; init; }
}
```

This is non-breaking — the property is nullable and defaults to `null` via `init`. All existing code that creates `PluginActionInput` without `Services` continues to work unchanged.

### Step 4: Verify `ActionContext` in SDK

**File:** `src/core/Gantri.Plugins.Sdk/ActionContext.cs`

`ActionContext.Services` is already typed as `IPluginServices?`. After Step 2, the SDK's `IPluginServices` inherits from the Abstractions version. The `ActionContext` type continues to compile because the SDK's `IPluginServices` is still resolvable in the `Gantri.Plugins.Sdk` namespace. Confirm with a build.

### Step 5: Add Connection Abstractions

This step creates two categories of types: **generic reusable interfaces** (any external service) and **Dataverse-specific contracts** (only for Dataverse).

#### 5a. Generic Connection Interfaces — `Gantri.Plugins.Sdk.Connections`

**New directory:** `src/core/Gantri.Plugins.Sdk/Connections/`

These are service-agnostic contracts. A future SharePoint or Azure DevOps provider would implement the same interfaces.

##### `IServiceConnection.cs`

```csharp
namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Represents an open, authenticated connection to an external service.
/// Managed by IConnectionProvider — plugins should NOT dispose these directly.
/// </summary>
public interface IServiceConnection : IAsyncDisposable
{
    string ProfileName { get; }
    string ServiceType { get; }
    string DisplayName { get; }
    bool IsValid { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
```

##### `IConnectionProvider.cs`

```csharp
namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Manages named connection profiles and a connection pool for a specific external service.
/// One provider per service type. Implement per-service marker interfaces
/// (e.g., IDataverseConnectionProvider) so plugins can resolve the specific provider they need.
/// </summary>
public interface IConnectionProvider
{
    string ServiceType { get; }
    IReadOnlyList<string> GetAvailableProfiles();
    string? GetActiveProfile();
    void SetActiveProfile(string profileName);
    Task<IServiceConnection> GetConnectionAsync(string profileName, CancellationToken ct = default);
    Task<IServiceConnection> GetActiveConnectionAsync(CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string profileName, CancellationToken ct = default);
}
```

##### `ConnectionProfile.cs`

```csharp
namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Minimal base model for a connection profile entry.
/// Service-specific profiles should extend this with their own fields
/// (e.g., DataverseConnectionProfile adds TenantId, AuthType, etc.).
/// </summary>
public class ConnectionProfile
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
}
```

#### 5b. Dataverse-Specific Contracts — `Gantri.Dataverse.Sdk`

**New project:** `src/core/Gantri.Dataverse.Sdk/`

This is a lightweight contracts project that Dataverse plugins reference. It defines Dataverse-specific types without pulling in the heavy `Microsoft.PowerPlatform.Dataverse.Client` NuGet. The implementation project (`Gantri.Dataverse`) references this.

##### `Gantri.Dataverse.Sdk.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>Gantri.Dataverse.Sdk</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Gantri.Plugins.Sdk\Gantri.Plugins.Sdk.csproj" />
  </ItemGroup>
</Project>
```

##### `IDataverseConnectionProvider.cs`

```csharp
namespace Gantri.Dataverse.Sdk;

using Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Dataverse-specific connection provider.
/// Plugins resolve via: context.Services.GetService&lt;IDataverseConnectionProvider&gt;()
/// Returns IDataverseServiceConnection instances with access to the underlying ServiceClient.
/// </summary>
public interface IDataverseConnectionProvider : IConnectionProvider
{
    /// <summary>Get a typed Dataverse connection to a specific profile.</summary>
    new Task<IDataverseServiceConnection> GetConnectionAsync(string profileName, CancellationToken ct = default);

    /// <summary>Get a typed Dataverse connection to the active profile.</summary>
    new Task<IDataverseServiceConnection> GetActiveConnectionAsync(CancellationToken ct = default);
}
```

##### `IDataverseServiceConnection.cs`

```csharp
namespace Gantri.Dataverse.Sdk;

using Gantri.Plugins.Sdk.Connections;

/// <summary>
/// A Dataverse-specific service connection.
/// Extends the generic IServiceConnection with Dataverse-aware members.
/// </summary>
public interface IDataverseServiceConnection : IServiceConnection
{
    /// <summary>The Dataverse environment URL (e.g., https://org.crm.dynamics.com).</summary>
    string EnvironmentUrl { get; }

    /// <summary>The organization unique name.</summary>
    string? OrganizationName { get; }
}
```

##### `DataverseConnectionProfile.cs`

```csharp
namespace Gantri.Dataverse.Sdk;

using Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Configuration for a single Dataverse environment connection.
/// Extends the generic ConnectionProfile with Dataverse/Azure AD-specific fields.
/// </summary>
public class DataverseConnectionProfile : ConnectionProfile
{
    /// <summary>
    /// Authentication type: "clientSecret", "deviceCode", "interactive", "azureCli", "certificate".
    /// </summary>
    public string AuthType { get; set; } = "clientSecret";

    /// <summary>Azure AD tenant ID (supports ${ENV_VAR} substitution).</summary>
    public string? TenantId { get; set; }

    /// <summary>Client/App registration ID for service principal or OAuth app.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret value (for clientSecret auth).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Certificate thumbprint or path (for certificate auth).</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Token cache duration in minutes (default: 60).</summary>
    public int TokenCacheDurationMinutes { get; set; } = 60;
}
```

**Why a separate `Gantri.Dataverse.Sdk` project:**

- Plugin authors reference `Gantri.Dataverse.Sdk` (lightweight, no heavy NuGet deps) — not the full `Gantri.Dataverse` implementation.
- The naming makes it unambiguous: `Gantri.Dataverse.Sdk` = contracts for Dataverse plugins. `Gantri.Plugins.Sdk.Connections` = generic connection contracts for any service.
- A future `Gantri.SharePoint.Sdk` would follow the same pattern: service-specific interfaces, service-specific profile model, generic base.

**Naming convention established:**

| Scope | Namespace | Example Types |
|-------|-----------|---------------|
| Generic (any service) | `Gantri.Plugins.Sdk.Connections` | `IConnectionProvider`, `IServiceConnection`, `ConnectionProfile` |
| Dataverse contracts | `Gantri.Dataverse.Sdk` | `IDataverseConnectionProvider`, `IDataverseServiceConnection`, `DataverseConnectionProfile` |
| Dataverse implementation | `Gantri.Dataverse` | `DataverseConnectionProvider`, `DataverseServiceConnection`, `DataverseTokenProvider` |
| Dataverse plugin tools | `Gantri.Dataverse.Tools` | `DataverseWhoAmIAction`, `DataverseListEntitiesAction`, `DataverseQueryRecordsAction` |

### Step 6: Implement `DefaultPluginServices`

**New file:** `src/core/Gantri.Plugins/DefaultPluginServices.cs`

Framework-level implementation backed by the host's DI container:

```csharp
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins;

public sealed class DefaultPluginServices : IPluginServices
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    public DefaultPluginServices(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
    }

    public IPluginLogger GetLogger(string categoryName)
        => new PluginLoggerAdapter(_loggerFactory.CreateLogger(categoryName));

    public string? GetConfig(string key)
        => _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?[key];

    public IChatClient? GetChatClient(string? modelAlias = null)
        => _serviceProvider.GetService<IChatClient>();

    public T? GetService<T>(string? name = null) where T : class
    {
        if (name is null)
            return _serviceProvider.GetService<T>();

        // Keyed service support (.NET 8+)
        if (_serviceProvider is IKeyedServiceProvider keyed)
            return keyed.GetKeyedService<T>(name);

        return _serviceProvider.GetService<T>();
    }
}
```

**Register in:** `src/core/Gantri.Plugins/PluginServiceExtensions.cs`

Add to the existing `AddGantriPlugins()`:

```csharp
services.AddSingleton<IPluginServices>(sp =>
    new DefaultPluginServices(sp, sp.GetRequiredService<ILoggerFactory>()));
```

### Step 7: Thread Services Through the Execution Pipeline

This is the most critical step — connecting the DI-backed `IPluginServices` to the point where plugin actions actually execute.

#### 7a. `ReflectionPluginActionAdapter` (Native loader)

**File:** `src/core/Gantri.Plugins.Native/ReflectionPluginActionAdapter.cs`

Update the **direct-cast path** (when types share the same ALC):

```csharp
// Line ~44, inside the if (_instance is ISdkPluginAction directAction) block:
var ctx = new ActionContext
{
    ActionName = input.ActionName,
    Parameters = input.Parameters,
    CancellationToken = cancellationToken,
    WorkingDirectory = input.WorkingDirectory,
    Services = input.Services  // NEW — pass through from PluginActionInput
};
```

Update the **reflection path** (`CreateActionContextReflection`):

```csharp
private object CreateActionContextReflection(PluginActionInput input, CancellationToken cancellationToken)
{
    var context = Activator.CreateInstance(_actionContextType)!;
    _actionContextType.GetProperty("ActionName")?.SetValue(context, input.ActionName);
    _actionContextType.GetProperty("Parameters")?.SetValue(context, input.Parameters);
    _actionContextType.GetProperty("CancellationToken")?.SetValue(context, cancellationToken);
    _actionContextType.GetProperty("WorkingDirectory")?.SetValue(context, input.WorkingDirectory);
    _actionContextType.GetProperty("Services")?.SetValue(context, input.Services);  // NEW
    return context;
}
```

**Why this works cross-ALC:** `IPluginServices` is defined in `Gantri.Abstractions` which loads in the default ALC. Both the framework and the plugin see the same type identity. The `SetValue` reflection call succeeds because the value's type matches the property's type across contexts.

#### 7b. `PluginActionFunction` (Bridge layer)

**File:** `src/integration/Gantri.Bridge/PluginActionFunction.cs`

Add a field and constructor parameter:

```csharp
private readonly IPluginServices? _pluginServices;

public PluginActionFunction(
    string pluginName,
    string actionName,
    string? description,
    JsonElement? parametersSchema,
    IPluginRouter pluginRouter,
    IPluginServices? pluginServices,       // NEW parameter
    string? workingDirectory = null,
    IToolApprovalHandler? approvalHandler = null,
    IReadOnlyDictionary<string, object?>? additionalParameters = null)
{
    // ... existing assignments ...
    _pluginServices = pluginServices;
}
```

Update `InvokeCoreAsync` to pass services:

```csharp
var result = await plugin.ExecuteActionAsync(_actionName, new PluginActionInput
{
    ActionName = _actionName,
    Parameters = parameters,
    WorkingDirectory = _workingDirectory,
    Services = _pluginServices          // NEW
}, cancellationToken);
```

#### 7c. `GantriAgentFactory` (Bridge layer)

**File:** `src/integration/Gantri.Bridge/GantriAgentFactory.cs`

Add field and constructor parameter:

```csharp
private readonly IPluginServices? _pluginServices;

public GantriAgentFactory(
    ModelProviderRegistry modelProviderRegistry,
    IPluginRouter pluginRouter,
    IMcpToolProvider mcpToolProvider,
    IHookPipeline hookPipeline,
    ILogger<GantriAgentFactory> logger,
    ILoggerFactory loggerFactory,
    IOptions<WorkingDirectoryOptions> workingDirectoryOptions,
    Func<string, AiModelOptions, IChatClient>? clientFactory = null,
    IToolApprovalHandler? approvalHandler = null,
    McpPermissionManager? mcpPermissionManager = null,
    IPluginServices? pluginServices = null)            // NEW
{
    // ... existing assignments ...
    _pluginServices = pluginServices;
}
```

Pass to `PluginActionFunction` in `CollectToolsAsync()` (around line 189):

```csharp
tools.Add(new PluginActionFunction(
    pluginName,
    action.Name,
    action.Description,
    action.Parameters,
    _pluginRouter,
    _pluginServices,                  // NEW — inserted after pluginRouter
    workingDirectory,
    _approvalHandler,
    agentAdditionalParams
));
```

#### 7d. `BridgeServiceExtensions` (DI wiring)

**File:** `src/integration/Gantri.Bridge/BridgeServiceExtensions.cs`

Resolve `IPluginServices` and pass to factory:

```csharp
services.AddSingleton<GantriAgentFactory>(sp =>
{
    // ... existing resolutions ...
    var pluginServices = sp.GetService<IPluginServices>();  // NEW

    return new GantriAgentFactory(
        registry,
        pluginRouter,
        mcpToolProvider,
        hookPipeline,
        logger,
        loggerFactory,
        workingDirectoryOptions,
        clientFactory,
        approvalHandler,
        mcpPermissionManager,
        pluginServices              // NEW
    );
});
```

### Step 8: Create `Gantri.Dataverse` Implementation Project

**New project:** `src/core/Gantri.Dataverse/`

This is the implementation project — it references `Gantri.Dataverse.Sdk` for the contracts and pulls in the heavy Microsoft NuGet packages. Only the host (CLI/API/Worker) references this project; plugins reference `Gantri.Dataverse.Sdk` instead.

```
Gantri.Dataverse/
├── Gantri.Dataverse.csproj
├── DataverseConnectionProvider.cs     (implements IDataverseConnectionProvider)
├── DataverseServiceConnection.cs      (implements IDataverseServiceConnection, wraps ServiceClient)
├── DataverseTokenProvider.cs          (auth strategy factory)
├── DataverseOptions.cs                (config model)
└── DataverseServiceExtensions.cs      (DI registration)
```

#### `Gantri.Dataverse.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>Gantri.Dataverse</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Gantri.Dataverse.Sdk\Gantri.Dataverse.Sdk.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.*" />
    <PackageReference Include="Azure.Identity" Version="1.*" />
  </ItemGroup>
</Project>
```

#### `DataverseOptions.cs`

Configuration model deserialized from `dataverse.yaml`:

```csharp
namespace Gantri.Dataverse;

using Gantri.Dataverse.Sdk;

public sealed class DataverseOptions
{
    public string? ActiveProfile { get; set; }
    public Dictionary<string, DataverseConnectionProfile> Profiles { get; set; } = new();
    public bool CacheConnections { get; set; } = true;
}
```

#### `DataverseServiceConnection.cs`

Wraps `ServiceClient` behind the `IDataverseServiceConnection` interface:

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;
using Gantri.Dataverse.Sdk;

namespace Gantri.Dataverse;

internal sealed class DataverseServiceConnection : IDataverseServiceConnection
{
    private readonly ServiceClient _client;

    public string ProfileName { get; }
    public string ServiceType => "dataverse";
    public string DisplayName { get; }
    public string EnvironmentUrl { get; }
    public string? OrganizationName => _client.ConnectedOrgUniqueName;
    public bool IsValid => _client.IsReady;

    internal DataverseServiceConnection(
        ServiceClient client,
        string profileName,
        string displayName,
        string environmentUrl)
    {
        _client = client;
        ProfileName = profileName;
        DisplayName = displayName;
        EnvironmentUrl = environmentUrl;
    }

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    // ServiceClient handles token refresh internally on each call

    /// <summary>Access the underlying ServiceClient for D365 SDK operations.</summary>
    internal ServiceClient Client => _client;

    public async ValueTask DisposeAsync() => _client.Dispose();
}
```

#### `DataverseTokenProvider.cs`

Creates `ServiceClient` instances using the appropriate auth strategy based on `DataverseConnectionProfile.AuthType`. Uses `Azure.Identity` credential types:

- `clientSecret` → `ClientSecretCredential` (requires TenantId, ClientId, ClientSecret)
- `deviceCode` → `DeviceCodeCredential` (requires TenantId, optional ClientId)
- `interactive` → `InteractiveBrowserCredential` (requires TenantId, optional ClientId)
- `azureCli` → `AzureCliCredential` (uses cached `az login` token)
- `certificate` → `ClientCertificateCredential` (requires TenantId, ClientId, CertificatePath)

Each creates a `ServiceClient` via `new ServiceClient(uri, credential, useUniqueInstance: false)`.

#### `DataverseConnectionProvider.cs`

Implements `IDataverseConnectionProvider`. Manages:

- `ConcurrentDictionary<string, DataverseServiceConnection>` for connection pooling
- Active profile tracking (in-memory, session-scoped)
- Connection creation via `DataverseTokenProvider`
- Stale connection eviction (check `IsValid` before returning cached)

#### `DataverseServiceExtensions.cs`

```csharp
using Gantri.Dataverse.Sdk;

public static class DataverseServiceExtensions
{
    public static IServiceCollection AddGantriDataverse(this IServiceCollection services)
    {
        services.AddSingleton<DataverseTokenProvider>();
        services.AddSingleton<IDataverseConnectionProvider>(sp =>
        {
            var config = sp.GetRequiredService<GantriConfigRoot>();
            var tokenProvider = sp.GetRequiredService<DataverseTokenProvider>();
            var logger = sp.GetRequiredService<ILogger<DataverseConnectionProvider>>();
            return new DataverseConnectionProvider(config.Dataverse, tokenProvider, logger);
        });
        return services;
    }
}
```

### Step 9: Configuration

#### New file: `config/dataverse.yaml`

```yaml
dataverse:
  active_profile: dev
  profiles:
    dev:
      name: dev
      url: https://org-dev.crm.dynamics.com
      auth_type: device_code
      tenant_id: ${AZURE_TENANT_ID}
      description: "Development environment"
    prod:
      name: prod
      url: https://org-prod.crm.dynamics.com
      auth_type: client_secret
      tenant_id: ${AZURE_TENANT_ID}
      client_id: ${DATAVERSE_CLIENT_ID}
      credential: ${DATAVERSE_CLIENT_SECRET}
      description: "Production CRM"
    customer-sandbox:
      name: customer-sandbox
      url: https://customer-sandbox.crm.dynamics.com
      auth_type: azure_cli
      description: "Customer sandbox (uses az login)"
```

#### Update: `config/gantri.yaml`

Add `dataverse.yaml` to the imports list:

```yaml
framework:
  imports:
    - ai.yaml
    - agents.yaml
    - plugins.yaml
    - telemetry.yaml
    - scheduling.yaml
    - mcp.yaml
    - worker.yaml
    - api.yaml
    - dataverse.yaml      # NEW
    - agents/*.yaml
    - workflows/*.yaml
```

#### Update: `src/core/Gantri.Configuration/GantriConfigurationExtensions.cs`

Add `DataverseOptions` to `GantriConfigRoot`:

```csharp
public sealed class GantriConfigRoot
{
    // ... existing fields ...
    public DataverseOptions Dataverse { get; set; } = new();
}
```

### Step 10: Example Dataverse Plugin

**New plugin:** `plugins/built-in/dataverse-tools/`

#### `manifest.json`

```json
{
    "name": "dataverse-tools",
    "version": "1.0.0",
    "type": "native",
    "description": "Dataverse tools using the official Microsoft Dataverse SDK",
    "entry": "Gantri.Dataverse.Tools.dll",
    "capabilities": {
        "required": ["http-request"],
        "optional": ["config-read"]
    },
    "exports": {
        "actions": [
            {
                "name": "who-am-i",
                "description": "Get current user and organization info from the active Dataverse environment",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "profile": {
                            "type": "string",
                            "description": "Optional profile name (uses active profile if omitted)"
                        }
                    },
                    "required": []
                }
            },
            {
                "name": "list-entities",
                "description": "List all entities/tables in the Dataverse environment",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "filter": {
                            "type": "string",
                            "description": "Optional filter by logical name prefix"
                        },
                        "custom_only": {
                            "type": "boolean",
                            "description": "Only return custom entities (default: false)"
                        }
                    },
                    "required": []
                }
            },
            {
                "name": "query-records",
                "description": "Query records using FetchXML",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "fetch_xml": {
                            "type": "string",
                            "description": "FetchXML query string"
                        }
                    },
                    "required": ["fetch_xml"]
                }
            }
        ],
        "hooks": []
    }
}
```

#### `Gantri.Dataverse.Tools.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Gantri.Dataverse.Tools</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\core\Gantri.Dataverse.Sdk\Gantri.Dataverse.Sdk.csproj" />
  </ItemGroup>
</Project>
```

**Note:** The plugin references `Gantri.Dataverse.Sdk` (lightweight contracts) — NOT `Gantri.Dataverse` (heavy implementation with ServiceClient). The plugin resolves `IDataverseConnectionProvider` through `context.Services.GetService<T>()` and works entirely through the SDK interfaces.

#### `DataverseWhoAmIAction.cs` (example pattern)

```csharp
using Gantri.Plugins.Sdk;
using Gantri.Dataverse.Sdk;
using Microsoft.Crm.Sdk.Messages;

namespace Gantri.Dataverse.Tools;

public sealed class DataverseWhoAmIAction : ISdkPluginAction
{
    public string ActionName => "who-am-i";
    public string Description => "Get current user and organization info from the active Dataverse environment";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)
    {
        var provider = context.Services?.GetService<IDataverseConnectionProvider>();
        if (provider is null)
            return ActionResult.Fail("Dataverse connection provider not available. Ensure dataverse.yaml is configured.");

        var profileName = context.Parameters.TryGetValue("profile", out var p) && p is string s ? s : null;

        try
        {
            var conn = profileName is not null
                ? await provider.GetConnectionAsync(profileName, ct)
                : await provider.GetActiveConnectionAsync(ct);

            return ActionResult.Ok(new
            {
                Profile = conn.ProfileName,
                Environment = conn.EnvironmentUrl,
                Organization = conn.OrganizationName,
                DisplayName = conn.DisplayName
            });
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Dataverse WhoAmI failed: {ex.Message}");
        }
    }
}
```

**Note on ServiceClient access:** The plugin works through `IDataverseServiceConnection` (from `Gantri.Dataverse.Sdk`). For operations that need the raw `ServiceClient` (like executing `WhoAmIRequest` directly), the built-in plugin can additionally reference `Gantri.Dataverse` to access `DataverseServiceConnection.Client`. Third-party plugins would expose SDK-level operations via the connection interface instead.

### Step 11: CLI Commands for Dataverse Connection Management

**New commands** under the existing `plugin` Spectre.Console branch:

```
gantri plugin dataverse profiles      → Table of all profiles (name, URL, auth type, active marker)
gantri plugin dataverse switch <name> → Switch active profile for current session
gantri plugin dataverse test [name]   → Test connection (uses active if name omitted)
gantri plugin dataverse current       → Show active profile details
```

**File:** `src/hosts/Gantri.Cli/Commands/DataverseProfilesCommand.cs`

Uses `IDataverseConnectionProvider.GetAvailableProfiles()` and renders a Spectre.Console table with columns: Profile Name, URL, Auth Type, Active (green dot marker).

**File:** `src/hosts/Gantri.Cli/Commands/DataverseSwitchCommand.cs`

Takes a profile name argument, calls `SetActiveProfile()`, confirms the switch.

**File:** `src/hosts/Gantri.Cli/Commands/DataverseTestCommand.cs`

Calls `TestConnectionAsync()` with a spinner, reports success/failure.

**File:** `src/hosts/Gantri.Cli/Commands/DataverseCurrentCommand.cs`

Shows the active profile's name, URL, auth type, and description.

**Update:** `src/hosts/Gantri.Cli/Program.cs` (around line 283):

```csharp
appConfig.AddBranch("plugin", plugin =>
{
    plugin.SetDescription("Manage plugins");
    plugin.AddCommand<PluginListCommand>("list")
        .WithDescription("List all discovered plugins");
    plugin.AddCommand<PluginInspectCommand>("inspect")
        .WithDescription("Show detailed information about a plugin");

    // NEW: Dataverse connection management
    plugin.AddBranch("dataverse", dataverse =>
    {
        dataverse.SetDescription("Dataverse connection management");
        dataverse.AddCommand<DataverseProfilesCommand>("profiles")
            .WithDescription("List all configured Dataverse profiles");
        dataverse.AddCommand<DataverseSwitchCommand>("switch")
            .WithDescription("Switch the active Dataverse profile");
        dataverse.AddCommand<DataverseTestCommand>("test")
            .WithDescription("Test a Dataverse connection");
        dataverse.AddCommand<DataverseCurrentCommand>("current")
            .WithDescription("Show the current active Dataverse profile");
    });
});
```

### Step 12: Solution and Host Registration

**Update:** `Gantri.slnx` — add new projects:
- `src/core/Gantri.Dataverse.Sdk/Gantri.Dataverse.Sdk.csproj`
- `src/core/Gantri.Dataverse/Gantri.Dataverse.csproj`
- `plugins/built-in/dataverse-tools/Gantri.Dataverse.Tools.csproj`

**Update:** `src/hosts/Gantri.Cli/Program.cs` — register Dataverse services:

```csharp
// After services.AddGantriPlugins() and config loading
services.AddGantriDataverse();
```

**Update:** `src/hosts/Gantri.Api/Program.cs` and `src/hosts/Gantri.Worker/Program.cs` — same registration for API and Worker hosts.

---

## 5. File Change Summary

### Files Modified (existing)

| File | Change |
|------|--------|
| `src/core/Gantri.Plugins.Sdk/IPluginServices.cs` | Replace definitions with re-export interfaces inheriting from Abstractions |
| `src/core/Gantri.Abstractions/Plugins/IPlugin.cs` | Add `Services` property to `PluginActionInput` |
| `src/core/Gantri.Plugins.Native/ReflectionPluginActionAdapter.cs` | Set `Services` in both direct-cast and reflection execution paths |
| `src/core/Gantri.Plugins/PluginServiceExtensions.cs` | Register `DefaultPluginServices` in DI |
| `src/integration/Gantri.Bridge/PluginActionFunction.cs` | Accept and pass `IPluginServices` |
| `src/integration/Gantri.Bridge/GantriAgentFactory.cs` | Accept and pass `IPluginServices` to `PluginActionFunction` |
| `src/integration/Gantri.Bridge/BridgeServiceExtensions.cs` | Resolve `IPluginServices` from DI and inject into factory |
| `src/core/Gantri.Configuration/GantriConfigurationExtensions.cs` | Add `DataverseOptions` to `GantriConfigRoot` |
| `src/hosts/Gantri.Cli/Program.cs` | Register Dataverse services + CLI commands |
| `config/gantri.yaml` | Import `dataverse.yaml` |
| `Gantri.slnx` | Add new projects |

### Files Created (new)

| File | Purpose |
|------|---------|
| **Abstractions layer** | |
| `src/core/Gantri.Abstractions/Plugins/IPluginServices.cs` | `IPluginServices`, `IPluginLogger`, `PluginLogLevel` (canonical definitions) |
| **Generic SDK contracts** | |
| `src/core/Gantri.Plugins.Sdk/Connections/IServiceConnection.cs` | Generic connection handle interface (any service) |
| `src/core/Gantri.Plugins.Sdk/Connections/IConnectionProvider.cs` | Generic connection management interface (any service) |
| `src/core/Gantri.Plugins.Sdk/Connections/ConnectionProfile.cs` | Base profile model (name, url, description) |
| **Dataverse SDK contracts** | |
| `src/core/Gantri.Dataverse.Sdk/Gantri.Dataverse.Sdk.csproj` | Lightweight Dataverse contracts project (no heavy NuGets) |
| `src/core/Gantri.Dataverse.Sdk/IDataverseConnectionProvider.cs` | Dataverse-specific connection provider interface |
| `src/core/Gantri.Dataverse.Sdk/IDataverseServiceConnection.cs` | Dataverse-specific connection interface (EnvironmentUrl, OrgName) |
| `src/core/Gantri.Dataverse.Sdk/DataverseConnectionProfile.cs` | Dataverse profile model (AuthType, TenantId, ClientId, etc.) |
| **Plugin services** | |
| `src/core/Gantri.Plugins/DefaultPluginServices.cs` | DI-backed `IPluginServices` implementation |
| **Dataverse implementation** | |
| `src/core/Gantri.Dataverse/Gantri.Dataverse.csproj` | Implementation project (Dataverse SDK + Azure.Identity NuGets) |
| `src/core/Gantri.Dataverse/DataverseOptions.cs` | YAML configuration model |
| `src/core/Gantri.Dataverse/DataverseConnectionProvider.cs` | Connection pool + profile management |
| `src/core/Gantri.Dataverse/DataverseServiceConnection.cs` | `ServiceClient` wrapper implementing `IDataverseServiceConnection` |
| `src/core/Gantri.Dataverse/DataverseTokenProvider.cs` | Auth strategy factory (5 auth types) |
| `src/core/Gantri.Dataverse/DataverseServiceExtensions.cs` | DI registration helper |
| **Dataverse plugin tools** | |
| `plugins/built-in/dataverse-tools/manifest.json` | Plugin manifest (3 actions) |
| `plugins/built-in/dataverse-tools/Gantri.Dataverse.Tools.csproj` | Plugin project (references Gantri.Dataverse.Sdk) |
| `plugins/built-in/dataverse-tools/DataverseWhoAmIAction.cs` | Example: WhoAmI |
| `plugins/built-in/dataverse-tools/DataverseListEntitiesAction.cs` | Example: list entities/tables |
| `plugins/built-in/dataverse-tools/DataverseQueryRecordsAction.cs` | Example: FetchXML query |
| **CLI commands** | |
| `src/hosts/Gantri.Cli/Commands/DataverseProfilesCommand.cs` | CLI: list profiles table |
| `src/hosts/Gantri.Cli/Commands/DataverseSwitchCommand.cs` | CLI: switch active profile |
| `src/hosts/Gantri.Cli/Commands/DataverseTestCommand.cs` | CLI: test connection |
| `src/hosts/Gantri.Cli/Commands/DataverseCurrentCommand.cs` | CLI: show current profile |
| **Configuration** | |
| `config/dataverse.yaml` | Dataverse profile configuration |

---

## 6. Data Flow Diagram

```
Agent sends tool call "dataverse-tools.who-am-i"
    │
    ▼
PluginActionFunction.InvokeCoreAsync()                          [Gantri.Bridge]
    │  creates PluginActionInput { Services = _pluginServices }
    ▼
IPlugin.ExecuteActionAsync()                                    [Gantri.Abstractions]
    │
    ▼
NativePlugin → ReflectionPluginActionAdapter.ExecuteAsync()     [Gantri.Plugins.Native]
    │  builds ActionContext { Services = input.Services }
    ▼
DataverseWhoAmIAction.ExecuteAsync(context)                     [Gantri.Dataverse.Tools]
    │  context.Services.GetService<IDataverseConnectionProvider>()
    ▼
DefaultPluginServices.GetService<T>()                           [Gantri.Plugins]
    │  _serviceProvider.GetService<IDataverseConnectionProvider>()
    ▼
DataverseConnectionProvider.GetActiveConnectionAsync()          [Gantri.Dataverse]
    │  looks up active profile → creates/returns pooled ServiceClient
    │  returns IDataverseServiceConnection
    ▼
ActionResult.Ok({ Profile, EnvironmentUrl, OrganizationName })
```

**Project reference chain:**

```
Plugin author references:
  Gantri.Dataverse.Tools.csproj → Gantri.Dataverse.Sdk → Gantri.Plugins.Sdk → Gantri.Abstractions

Host (CLI/API/Worker) registers:
  Gantri.Dataverse → Gantri.Dataverse.Sdk → Gantri.Plugins.Sdk → Gantri.Abstractions
```

---

## 7. Verification Checklist

1. **Build:** `dotnet build Gantri.slnx` — all projects compile with no errors
2. **Existing tests pass:** `dotnet test` — the `Services` property is nullable so no existing behavior changes
3. **Plugin services wiring:** Unit test creating `DefaultPluginServices` from a `ServiceCollection`, registering a mock `IDataverseConnectionProvider`, verifying `GetService<IDataverseConnectionProvider>()` returns it
4. **Adapter reflection test:** Unit test verifying `ReflectionPluginActionAdapter` correctly sets `Services` on `ActionContext` when `PluginActionInput.Services` is non-null
5. **D365 plugin integration:** Configure `dataverse.yaml` with a test profile, run `gantri plugin d365 test dev`, verify connection succeeds
6. **Agent round-trip:** Configure an agent with `d365-tools` plugin, send "who am I", verify it resolves the connection provider and returns org info

---

## 8. Future Considerations

- **Plugin groups (Approach 2):** If raw `ServiceClient` performance becomes critical, add shared ALC support for plugin families. The `IConnectionProvider` abstraction remains the primary API; shared ALC is an optimization.
- **Additional connection providers:** SharePoint (`Gantri.SharePoint.Sdk` + `Gantri.SharePoint`), Azure DevOps (`Gantri.AzureDevOps.Sdk` + `Gantri.AzureDevOps`), Dynamics 365 Finance — all follow the same `IConnectionProvider` base with service-specific SDK projects.
- **Profile persistence:** `SetActiveProfile()` is currently session-scoped. Could add YAML write-back for persistent switching.
- **Interactive profile selection:** A Spectre.Console `SelectionPrompt` in the REPL for quick switching via `/d365 switch`.
- **Agent-driven switching:** Expose `set-active-profile` as a plugin action so agents can programmatically switch environments mid-conversation ("now check the same entity in production").
- **Credential encryption:** The JavaScript example uses filesystem-based credential storage with `0o600` permissions. For .NET, consider DPAPI (Windows), Keychain (macOS), or libsecret (Linux) for secure credential storage beyond environment variables.
