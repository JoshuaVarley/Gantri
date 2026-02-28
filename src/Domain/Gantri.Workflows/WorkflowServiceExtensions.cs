using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gantri.Workflows;

public static class WorkflowServiceExtensions
{
    public static IServiceCollection AddGantriWorkflows(
        this IServiceCollection services,
        string dataDir = "./data"
    )
    {
        // Step handlers
        services.AddSingleton<IStepHandler, AgentStepHandler>();
        services.AddSingleton<IStepHandler, PluginStepHandler>();
        services.AddSingleton<IStepHandler, ConditionStepHandler>();
        services.AddSingleton<IStepHandler, ApprovalStepHandler>();

        services.AddSingleton<StepExecutor>();

        // ParallelStepHandler uses Func<StepExecutor> to break the circular dependency:
        // StepExecutor → IEnumerable<IStepHandler> → ParallelStepHandler → StepExecutor
        services.AddSingleton<IStepHandler>(sp => new ParallelStepHandler(() =>
            sp.GetRequiredService<StepExecutor>()
        ));

        // Workflow state manager for persistence
        services.AddSingleton<WorkflowStateManager>(sp =>
        {
            return new WorkflowStateManager(
                dataDir,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkflowStateManager>()
            );
        });

        // Legacy WorkflowEngine — registered as concrete type so Bridge can resolve it,
        // and as IWorkflowEngine as the default (Bridge overrides with AfWorkflowEngine).
        services.AddSingleton<WorkflowEngine>(sp =>
        {
            return new WorkflowEngine(
                sp.GetRequiredService<IWorkflowDefinitionRegistry>(),
                sp.GetRequiredService<StepExecutor>(),
                sp.GetRequiredService<Gantri.Abstractions.Hooks.IHookPipeline>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkflowEngine>(),
                sp.GetService<WorkflowStateManager>()
            );
        });

        services.AddSingleton<ILegacyWorkflowEngine>(sp => sp.GetRequiredService<WorkflowEngine>());
        services.AddSingleton<IWorkflowEngine>(sp => sp.GetRequiredService<WorkflowEngine>());

        return services;
    }
}
