using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Scheduling;

/// <summary>
/// Seeds TickerQ cron tickers from YAML job definitions on startup.
/// </summary>
public static class ScheduledJobRegistry
{
    public static async Task SeedJobsAsync(
        ICronTickerManager<CronTickerEntity> cronManager,
        Dictionary<string, ScheduledJobDefinition> jobs,
        ILogger logger)
    {
        foreach (var (name, def) in jobs)
        {
            if (!def.Enabled || string.IsNullOrWhiteSpace(def.Cron))
            {
                logger.LogDebug("Skipping disabled/unconfigured job '{Job}'", name);
                continue;
            }

            var functionName = def.Type switch
            {
                "workflow" => "gantri_workflow_job",
                "agent" => "gantri_agent_job",
                "plugin" => "gantri_plugin_job",
                _ => null
            };

            if (functionName is null)
            {
                logger.LogWarning("Unknown job type '{Type}' for job '{Job}'", def.Type, name);
                continue;
            }

            var request = CreateTickerRequest(name, def);

            var entity = new CronTickerEntity
            {
                Function = functionName,
                Expression = def.Cron,
                Request = request is not null ? TickerHelper.CreateTickerRequest(request) : null,
                Retries = 3,
                RetryIntervals = [60, 300, 900]
            };

            var result = await cronManager.AddAsync(entity);
            if (result is not null && result.IsSucceeded)
            {
                logger.LogInformation("Seeded cron job '{Job}' ({Type}) with expression '{Cron}'",
                    name, def.Type, def.Cron);
            }
            else
            {
                logger.LogWarning("Failed to seed job '{Job}' ({Type}): {Error}",
                    name, def.Type, result?.Exception?.Message ?? "Unknown error");
            }
        }
    }

    private static object? CreateTickerRequest(string jobName, ScheduledJobDefinition def)
    {
        return def.Type switch
        {
            "workflow" => new WorkflowJobPayload
            {
                WorkflowName = def.Workflow ?? jobName,
                Parameters = def.Parameters
            },
            "agent" => new AgentJobPayload
            {
                AgentName = def.Agent ?? jobName,
                Input = def.Input
            },
            "plugin" => new PluginJobPayload
            {
                PluginName = def.Plugin ?? string.Empty,
                ActionName = def.Action ?? string.Empty,
                Parameters = def.Parameters
            },
            _ => null
        };
    }
}
