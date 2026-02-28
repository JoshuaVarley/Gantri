using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Scheduling;

public static class SchedulingServiceExtensions
{
    public static IServiceCollection AddGantriScheduling(
        this IServiceCollection services,
        SchedulingStorageOptions? storageOptions = null)
    {
        var connectionString = storageOptions?.ConnectionString ?? "Data Source=data/gantri-scheduling.db";

        // Ensure the directory for the SQLite database exists
        var dbPath = connectionString.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);

        services.AddTickerQ(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseTickerQDbContext<TickerQDbContext>(db =>
                    db.UseSqlite(connectionString));
            });

            options.ConfigureScheduler(scheduler =>
            {
                scheduler.MaxConcurrency = Environment.ProcessorCount * 2;
                scheduler.NodeIdentifier = Environment.MachineName;
            });
        });

        services.AddSingleton<TickerJobFunctions>();

        services.AddSingleton<JobRunner>(sp =>
        {
            var hookPipeline = sp.GetRequiredService<Gantri.Abstractions.Hooks.IHookPipeline>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobRunner>();
            var workflowEngine = sp.GetService<Gantri.Abstractions.Workflows.IWorkflowEngine>();
            var agentOrchestrator = sp.GetService<Gantri.Abstractions.Agents.IAgentOrchestrator>();
            var pluginRouter = sp.GetService<Gantri.Abstractions.Plugins.IPluginRouter>();
            return new JobRunner(hookPipeline, logger, workflowEngine, agentOrchestrator, pluginRouter);
        });

        services.AddSingleton<JobScheduler>(sp =>
        {
            var jobs = sp.GetService<Dictionary<string, ScheduledJobDefinition>>()
                ?? new Dictionary<string, ScheduledJobDefinition>();
            return new JobScheduler(
                jobs,
                sp.GetRequiredService<ICronTickerManager<CronTickerEntity>>(),
                sp.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobScheduler>());
        });
        services.AddSingleton<IJobScheduler>(sp => sp.GetRequiredService<JobScheduler>());

        return services;
    }
}
