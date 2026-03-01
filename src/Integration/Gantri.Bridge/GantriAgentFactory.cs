using System.Text.Json;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.AI;
using Gantri.Mcp;
using Gantri.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gantri.Bridge;

/// <summary>
/// Central factory that builds AF <see cref="AIAgent"/> instances from Gantri <see cref="AgentDefinition"/>.
/// Reads config, builds IChatClient from provider, collects plugin/MCP tools, applies hook middleware,
/// and calls <c>.AsAIAgent()</c> to produce an AF agent.
/// </summary>
public sealed class GantriAgentFactory
{
    private readonly ModelProviderRegistry _modelProviderRegistry;
    private readonly IPluginRouter _pluginRouter;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly IHookPipeline _hookPipeline;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly McpPermissionManager? _mcpPermissionManager;
    private readonly Func<string, AiModelOptions, IChatClient>? _clientFactory;
    private readonly ILogger<GantriAgentFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _defaultWorkingDirectory;
    private readonly IPluginServices? _pluginServices;
    private readonly bool _enableSensitiveData;

    public GantriAgentFactory(
        ModelProviderRegistry modelProviderRegistry,
        IPluginRouter pluginRouter,
        IMcpToolProvider mcpToolProvider,
        IHookPipeline hookPipeline,
        ILogger<GantriAgentFactory> logger,
        ILoggerFactory loggerFactory,
        IOptions<WorkingDirectoryOptions> workingDirectoryOptions,
        IOptions<TelemetryOptions> telemetryOptions,
        Func<string, AiModelOptions, IChatClient>? clientFactory = null,
        IToolApprovalHandler? approvalHandler = null,
        McpPermissionManager? mcpPermissionManager = null,
        IPluginServices? pluginServices = null
    )
    {
        _modelProviderRegistry = modelProviderRegistry;
        _pluginRouter = pluginRouter;
        _mcpToolProvider = mcpToolProvider;
        _hookPipeline = hookPipeline;
        _approvalHandler = approvalHandler;
        _mcpPermissionManager = mcpPermissionManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _clientFactory = clientFactory;
        _defaultWorkingDirectory = workingDirectoryOptions.Value.DefaultDirectory;
        _pluginServices = pluginServices;
        _enableSensitiveData = telemetryOptions.Value.Traces.EnableSensitiveData;
    }

    /// <summary>
    /// Builds an AF <see cref="AIAgent"/> from a Gantri <see cref="AgentDefinition"/>.
    /// </summary>
    public async Task<AIAgent> CreateAgentAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = GantriActivitySources.Agents.StartActivity(
            "gantri.bridge.create_agent"
        );
        activity?.SetTag(GantriSemanticConventions.AgentName, definition.Name);
        activity?.SetTag(GantriSemanticConventions.AgentProvider, definition.Provider ?? "default");
        activity?.SetTag(GantriSemanticConventions.AgentModel, definition.Model);

        // 1. Build IChatClient from provider
        var chatClient = CreateChatClient(definition);

        // 2. Apply hook middleware
        var hookedClient = chatClient.WithHooks(definition.Name, _hookPipeline);

        // 3. Apply resilience (retry + timeout) and M.E.AI OpenTelemetry + logging middleware
        //    RetryingChatClient: Polly-based retry (3x exponential backoff) + 60s timeout for transient Azure errors
        //    UseOpenTelemetry: emits "chat" spans with model info, token usage (GenAI semantic conventions)
        //    UseLogging: emits structured logs for each chat completion (visible in Aspire Structured Logs)
        var instrumentedClient = hookedClient
            .AsBuilder()
            .Use(inner => new RetryingChatClient(
                inner,
                _loggerFactory.CreateLogger<RetryingChatClient>()
            ))
            .UseOpenTelemetry(loggerFactory: _loggerFactory, sourceName: "Gantri.Agents",
                configure: cfg => cfg.EnableSensitiveData = _enableSensitiveData)
            .UseLogging(_loggerFactory)
            .Build();

        // 4. Collect tools from plugins and MCP servers
        var tools = await CollectToolsAsync(definition, cancellationToken);

        _logger.LogInformation(
            "Creating AF agent '{Agent}' with {ToolCount} tools (model: {Model}, provider: {Provider})",
            definition.Name,
            tools.Count,
            definition.Model,
            definition.Provider ?? "default"
        );

        // 5. Create AF AIAgent with agent-level OpenTelemetry instrumentation
        //    (emits "invoke_agent" and "execute_tool" spans per GenAI semantic conventions)
        var instructions = definition.SystemPrompt ?? "You are a helpful assistant.";
        var agent = instrumentedClient.AsAIAgent(
            instructions: instructions,
            name: definition.Name,
            tools: tools
        );

        return agent.AsBuilder()
            .UseOpenTelemetry(sourceName: "Gantri.Agents",
                configure: cfg => cfg.EnableSensitiveData = _enableSensitiveData)
            .Build();
    }

    private IChatClient CreateChatClient(AgentDefinition definition)
    {
        var (providerName, model) = _modelProviderRegistry.ResolveModel(
            definition.Model,
            definition.Provider
        );

        if (_clientFactory is not null)
            return _clientFactory(providerName, model);

        // Fallback: try to build client from provider options
        var providerOpts = _modelProviderRegistry.GetProvider(providerName)
            ?? throw new InvalidOperationException(
                $"No IChatClient factory configured for provider '{providerName}'."
            );

        return ChatClientFactory.Create(providerName, providerOpts, model);
    }

    private async Task<List<AITool>> CollectToolsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken
    )
    {
        var tools = new List<AITool>();
        var workingDirectory = definition.WorkingDirectory ?? _defaultWorkingDirectory;
        var allowedActions = BuildAllowedActions(definition.AllowedActions);
        var enforceAllowedActions = allowedActions.Count > 0;
        var enforceMcpPermissions =
            _mcpPermissionManager?.GetAllowedServers(definition.Name).Count > 0;

        // Build additional parameters for framework-injected context (e.g., AllowedCommands for shell-exec)
        IReadOnlyDictionary<string, object?>? agentAdditionalParams = null;
        if (definition.AllowedCommands.Count > 0)
        {
            agentAdditionalParams = new Dictionary<string, object?>
            {
                ["__allowed_commands"] = definition.AllowedCommands
            };
        }

        // Plugin tools
        foreach (var pluginName in definition.Plugins)
        {
            if (enforceAllowedActions && !HasAllowedActionsForPrefix(allowedActions, pluginName))
            {
                _logger.LogInformation(
                    "Skipping plugin '{Plugin}' for agent '{Agent}' because allowed_actions does not include it",
                    pluginName,
                    definition.Name
                );
                continue;
            }

            try
            {
                var plugin = await _pluginRouter.ResolveAsync(pluginName, cancellationToken);
                foreach (var action in plugin.Manifest.Exports.Actions)
                {
                    var toolName = $"{pluginName}.{action.Name}";
                    if (enforceAllowedActions && !IsAllowedAction(toolName, allowedActions))
                    {
                        _logger.LogInformation(
                            "Skipping tool '{Tool}' for agent '{Agent}' because it is not in allowed_actions",
                            toolName,
                            definition.Name
                        );
                        continue;
                    }

                    tools.Add(
                        new PluginActionFunction(
                            pluginName,
                            action.Name,
                            action.Description,
                            action.Parameters,
                            _pluginRouter,
                            _pluginServices,
                            workingDirectory,
                            _approvalHandler,
                            agentAdditionalParams
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to resolve plugin '{Plugin}' for tool registration",
                    pluginName
                );
            }
        }

        // MCP tools
        foreach (var serverName in definition.McpServers)
        {
            if (enforceAllowedActions && !HasAllowedActionsForPrefix(allowedActions, serverName))
            {
                _logger.LogInformation(
                    "Skipping MCP server '{Server}' for agent '{Agent}' because allowed_actions does not include it",
                    serverName,
                    definition.Name
                );
                continue;
            }

            if (
                enforceMcpPermissions
                && _mcpPermissionManager is not null
                && !_mcpPermissionManager.IsAllowed(definition.Name, serverName)
            )
            {
                _logger.LogWarning(
                    "Skipping MCP server '{Server}' for agent '{Agent}' because it is not permitted",
                    serverName,
                    definition.Name
                );
                continue;
            }

            try
            {
                var mcpTools = await _mcpToolProvider.GetToolsAsync(serverName, cancellationToken);
                foreach (var mcpTool in mcpTools)
                {
                    var toolName = $"{mcpTool.ServerName}.{mcpTool.ToolName}";
                    if (enforceAllowedActions && !IsAllowedAction(toolName, allowedActions))
                    {
                        _logger.LogInformation(
                            "Skipping tool '{Tool}' for agent '{Agent}' because it is not in allowed_actions",
                            toolName,
                            definition.Name
                        );
                        continue;
                    }

                    JsonElement? schema = null;
                    if (mcpTool.InputSchema is not null)
                    {
                        using var doc = JsonDocument.Parse(mcpTool.InputSchema);
                        schema = doc.RootElement.Clone();
                    }
                    tools.Add(
                        new McpToolFunction(
                            mcpTool.ServerName,
                            mcpTool.ToolName,
                            mcpTool.Description,
                            schema,
                            _mcpToolProvider,
                            _approvalHandler
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to get MCP tools from server '{Server}'",
                    serverName
                );
            }
        }

        return tools;
    }

    private static HashSet<string> BuildAllowedActions(IEnumerable<string> allowedActions)
    {
        return allowedActions
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Select(static action => action.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasAllowedActionsForPrefix(
        IReadOnlySet<string> allowedActions,
        string prefix
    )
    {
        var dotPrefix = prefix + ".";
        var wildcardPrefix = dotPrefix + "*";

        foreach (var allowed in allowedActions)
        {
            if (
                allowed.Equals(wildcardPrefix, StringComparison.OrdinalIgnoreCase)
                || allowed.StartsWith(dotPrefix, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedAction(string toolName, IReadOnlySet<string> allowedActions)
    {
        if (allowedActions.Contains(toolName))
            return true;

        var separator = toolName.IndexOf('.');
        if (separator <= 0)
            return false;

        var wildcard = toolName[..separator] + ".*";
        return allowedActions.Contains(wildcard);
    }
}
