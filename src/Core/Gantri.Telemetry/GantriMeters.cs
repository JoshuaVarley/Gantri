using System.Diagnostics.Metrics;

namespace Gantri.Telemetry;

public static class GantriMeters
{
    private static readonly Meter Meter = new("Gantri.Metrics", "1.0.0");

    // Agent metrics
    public static readonly UpDownCounter<long> AgentSessionsActive =
        Meter.CreateUpDownCounter<long>("gantri.agent.sessions.active", "{sessions}", "Currently active agent sessions");

    public static readonly Counter<long> AgentSessionsTotal =
        Meter.CreateCounter<long>("gantri.agent.sessions.total", "{sessions}", "Total sessions created");

    // Conversation metrics
    public static readonly Histogram<double> AgentConversationDuration =
        Meter.CreateHistogram<double>("gantri.agent.conversation.duration", "ms", "End-to-end conversation duration");

    public static readonly Counter<long> AgentMessagesTotal =
        Meter.CreateCounter<long>("gantri.agent.messages.total", "{messages}", "Total messages sent across all conversations");

    // AI metrics
    public static readonly Counter<long> AiCompletionsTotal =
        Meter.CreateCounter<long>("gantri.ai.completions.total", "{completions}", "Total model completions");

    public static readonly Histogram<double> AiCompletionsDuration =
        Meter.CreateHistogram<double>("gantri.ai.completions.duration", "ms", "Model call latency distribution");

    public static readonly Counter<long> AiTokensTotal =
        Meter.CreateCounter<long>("gantri.ai.tokens.total", "{tokens}", "Total tokens consumed");

    // Plugin metrics
    public static readonly UpDownCounter<long> PluginsLoaded =
        Meter.CreateUpDownCounter<long>("gantri.plugins.loaded", "{plugins}", "Currently loaded plugins");

    public static readonly Counter<long> PluginActionsTotal =
        Meter.CreateCounter<long>("gantri.plugins.actions.total", "{actions}", "Total plugin actions invoked");

    public static readonly Histogram<double> PluginActionsDuration =
        Meter.CreateHistogram<double>("gantri.plugins.actions.duration", "ms", "Plugin action latency");

    public static readonly Counter<long> PluginActionsErrors =
        Meter.CreateCounter<long>("gantri.plugins.actions.errors", "{errors}", "Plugin action failures");

    // Hook metrics
    public static readonly Counter<long> HookExecutionsTotal =
        Meter.CreateCounter<long>("gantri.hooks.executions.total", "{executions}", "Total hook executions");

    public static readonly Histogram<double> HookExecutionsDuration =
        Meter.CreateHistogram<double>("gantri.hooks.executions.duration", "ms", "Hook pipeline latency");

    public static readonly Counter<long> HookCancellations =
        Meter.CreateCounter<long>("gantri.hooks.cancellations", "{cancellations}", "Operations cancelled by hooks");

    // Workflow metrics
    public static readonly UpDownCounter<long> WorkflowsActive =
        Meter.CreateUpDownCounter<long>("gantri.workflows.active", "{workflows}", "Currently running workflows");

    public static readonly Counter<long> WorkflowStepsTotal =
        Meter.CreateCounter<long>("gantri.workflows.steps.total", "{steps}", "Total workflow steps executed");

    // Scheduler metrics
    public static readonly Counter<long> SchedulerJobsTotal =
        Meter.CreateCounter<long>("gantri.scheduler.jobs.total", "{jobs}", "Total job executions");

    public static readonly Histogram<double> SchedulerJobsDuration =
        Meter.CreateHistogram<double>("gantri.scheduler.jobs.duration", "ms", "Job execution latency");

    // MCP metrics
    public static readonly Counter<long> McpCallsTotal =
        Meter.CreateCounter<long>("gantri.mcp.calls.total", "{calls}", "Total MCP tool calls");

    public static readonly Histogram<double> McpCallsDuration =
        Meter.CreateHistogram<double>("gantri.mcp.calls.duration", "ms", "MCP call latency");

    public static string MeterName => Meter.Name;
}
