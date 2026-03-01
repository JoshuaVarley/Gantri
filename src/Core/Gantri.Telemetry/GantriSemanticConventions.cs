namespace Gantri.Telemetry;

public static class GantriSemanticConventions
{
    // Agent attributes
    public const string AgentName = "gantri.agent.name";
    public const string AgentSessionId = "gantri.agent.session_id";
    public const string AgentProvider = "gantri.agent.provider";
    public const string AgentModel = "gantri.agent.model";
    public const string AgentConversationId = "gantri.agent.conversation_id";
    public const string AgentMessageIndex = "gantri.agent.message_index";

    // GenAI semantic convention aliases (standard — set alongside Gantri attributes)
    // https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-agent-spans/
    public const string GenAiConversationId = "gen_ai.conversation.id";
    public const string GenAiAgentName = "gen_ai.agent.name";
    public const string GenAiAgentId = "gen_ai.agent.id";
    public const string GenAiOperationName = "gen_ai.operation.name";

    // AI attributes
    public const string AiTokensInput = "gantri.ai.tokens.input";
    public const string AiTokensOutput = "gantri.ai.tokens.output";
    public const string AiTokensTotal = "gantri.ai.tokens.total";
    public const string AiDurationMs = "gantri.ai.duration_ms";

    // Plugin attributes
    public const string PluginName = "gantri.plugin.name";
    public const string PluginType = "gantri.plugin.type";
    public const string PluginAction = "gantri.plugin.action";
    public const string PluginFuelConsumed = "gantri.plugin.fuel_consumed";
    public const string PluginMemoryBytes = "gantri.plugin.memory_bytes";

    // Hook attributes
    public const string HookEvent = "gantri.hook.event";
    public const string HookName = "gantri.hook.name";
    public const string HookCancelled = "gantri.hook.cancelled";

    // Workflow attributes
    public const string WorkflowName = "gantri.workflow.name";
    public const string WorkflowStepId = "gantri.workflow.step_id";
    public const string WorkflowStepType = "gantri.workflow.step_type";

    // Scheduler attributes
    public const string SchedulerJob = "gantri.scheduler.job";
    public const string SchedulerJobType = "gantri.scheduler.job_type";
    public const string SchedulerTrigger = "gantri.scheduler.trigger";

    // MCP attributes
    public const string McpServer = "gantri.mcp.server";
    public const string McpTool = "gantri.mcp.tool";
    public const string McpTransport = "gantri.mcp.transport";
}
