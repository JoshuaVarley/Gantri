using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gantri.Configuration;

public sealed class ConfigValidator
{
    private readonly ILogger<ConfigValidator> _logger;

    public ConfigValidator(ILogger<ConfigValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates configuration semantically. Returns list of errors. Logs warnings for non-critical issues.
    /// </summary>
    public IReadOnlyList<string> Validate(GantriConfigRoot config)
    {
        var errors = new List<string>();

        ValidateProviders(config, errors);
        ValidateAgents(config, errors);
        ValidateWorkflows(config, errors);
        ValidateMcp(config, errors);

        return errors;
    }

    private void ValidateProviders(GantriConfigRoot config, List<string> errors)
    {
        foreach (var (providerName, providerOpts) in config.Ai.Providers)
        {
            if (string.IsNullOrWhiteSpace(providerOpts.Endpoint) && string.IsNullOrWhiteSpace(providerOpts.BaseUrl))
                errors.Add($"Provider '{providerName}' has no endpoint or base_url configured");

            if (string.IsNullOrWhiteSpace(providerOpts.ApiKey))
                _logger.LogWarning("Provider '{ProviderName}' has no api_key configured", providerName);

            foreach (var (modelAlias, modelOpts) in providerOpts.Models)
            {
                if (string.IsNullOrWhiteSpace(modelOpts.Id))
                    errors.Add($"Model '{modelAlias}' in provider '{providerName}' has no id");

                if (!string.Equals(modelOpts.ApiType, "chat", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(modelOpts.ApiType, "responses", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Model '{modelAlias}' in provider '{providerName}' has invalid api_type '{modelOpts.ApiType}' (expected 'chat' or 'responses')");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(config.Ai.DefaultModel))
        {
            var modelFound = config.Ai.Providers.Values
                .Any(p => p.Models.ContainsKey(config.Ai.DefaultModel));
            if (!modelFound)
                errors.Add($"Default model '{config.Ai.DefaultModel}' not found in any provider");
        }
    }

    private void ValidateAgents(GantriConfigRoot config, List<string> errors)
    {
        foreach (var (name, def) in config.Agents)
        {
            if (string.IsNullOrWhiteSpace(def.Model))
            {
                errors.Add($"Agent '{name}' has no model configured");
                continue;
            }

            if (def.Provider is not null && !config.Ai.Providers.ContainsKey(def.Provider))
            {
                errors.Add($"Agent '{name}' references unknown provider '{def.Provider}'");
            }

            if (def.Provider is not null)
            {
                if (config.Ai.Providers.TryGetValue(def.Provider, out var providerOpts)
                    && !providerOpts.Models.ContainsKey(def.Model))
                {
                    errors.Add($"Agent '{name}' references model '{def.Model}' not found in provider '{def.Provider}'");
                }
            }
            else
            {
                var modelFound = config.Ai.Providers.Values
                    .Any(p => p.Models.ContainsKey(def.Model));
                if (!modelFound)
                    errors.Add($"Agent '{name}' references model '{def.Model}' not found in any provider");
            }
        }
    }

    private void ValidateWorkflows(GantriConfigRoot config, List<string> errors)
    {
        foreach (var (name, def) in config.Workflows)
        {
            foreach (var step in def.Steps)
            {
                ValidateWorkflowStep(config, name, step, errors);
            }
        }
    }

    private void ValidateWorkflowStep(GantriConfigRoot config, string workflowName, WorkflowStepDefinition step, List<string> errors)
    {
        if (step.Agent is not null && !config.Agents.ContainsKey(step.Agent))
            errors.Add($"Workflow '{workflowName}' step '{step.Id}' references unknown agent '{step.Agent}'");

        if (step.Plugin is not null)
            _logger.LogWarning("Workflow '{WorkflowName}' step '{StepId}' references plugin '{Plugin}' — cannot verify at config time",
                workflowName, step.Id, step.Plugin);

        // Recurse into nested steps (e.g. parallel)
        foreach (var nested in step.Steps)
        {
            ValidateWorkflowStep(config, workflowName, nested, errors);
        }
    }

    private void ValidateMcp(GantriConfigRoot config, List<string> errors)
    {
        foreach (var (serverName, serverDef) in config.Mcp.Servers)
        {
            if (string.Equals(serverDef.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(serverDef.Command))
                    errors.Add($"MCP server '{serverName}' uses stdio transport but has no command configured");
            }
        }
    }
}
