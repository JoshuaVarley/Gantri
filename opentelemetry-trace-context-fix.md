# OpenTelemetry Trace Context Fix — Implementation Plan

**Author:** Joshua Varley
**Date:** March 1, 2026
**Status:** Draft
**Target Framework:** Gantri (.NET 10)

---

## 1. Problem Statement

Agent chat sessions in Gantri produce **fragmented traces** — each user message, agent creation, and tool call appears as an independent trace root in the telemetry backend (Aspire, Jaeger, etc.) instead of nesting under a single unified conversation trace.

This makes it impossible to:

- See the full lifecycle of an agent conversation in a single trace waterfall
- Measure end-to-end latency across a multi-turn session
- Correlate tool calls, model completions, and hook executions back to the conversation that triggered them
- Debug issues in group chat orchestration where multiple agents interact sequentially

### Root Cause

The `System.Diagnostics.Activity` API uses `Activity.Current` (backed by `AsyncLocal<Activity>`) to automatically parent new spans under the current ambient span. When `StartActivity()` is called and `Activity.Current` is `null`, the new activity becomes a **trace root** — starting a brand new, disconnected trace.

The problem manifests in two places:

**CLI Host (`AgentRunCommand`):** The interactive loop calls `session.SendMessageAsync()` repeatedly, but there is no wrapping `Activity` around the session. Each `SendMessageAsync` call creates a `gantri.agent.session` span with no parent, producing a separate trace per message.

**API Host (`Gantri.Api`):** Each HTTP request to the AG-UI endpoint gets its own ASP.NET Core `http.server` span. Multi-turn conversations arrive as separate HTTP requests, so each turn is a separate trace. The `AfAgentSession` is reconstructed per-request by the AG-UI protocol, so there is no shared ambient context across turns.

**Group Chat (`GroupChatOrchestrator`):** The orchestrator does create a `gantri.bridge.group_chat` span, and the child operations (agent creation, session runs) should nest under it because `Activity.Current` flows through `async/await`. However, the `.UseOpenTelemetry()` middleware on `IChatClient` and `AIAgent` creates spans from `Microsoft.Extensions.AI` and `Microsoft.Agents.AI` activity sources — these may not correctly inherit the parent if the middleware creates activities on a different `ActivitySource` that isn't being listened to.

### Current Span Inventory

Every `StartActivity()` call in the codebase:

| Span Name | Source | File | Parent Context |
|-----------|--------|------|---------------|
| `gantri.agent.create_session` | `Gantri.Agents` | `AfAgentOrchestrator.cs:36` | Ambient (often none) |
| `gantri.bridge.create_agent` | `Gantri.Agents` | `GantriAgentFactory.cs:68` | Ambient |
| `gantri.agent.create_session` | `Gantri.Agents` | `AfAgentSession.cs:37` | Ambient |
| `gantri.agent.session` | `Gantri.Agents` | `AfAgentSession.cs:53` | Ambient (often none — **root cause**) |
| `gantri.agent.session.streaming` | `Gantri.Agents` | `AfAgentSession.cs:81` | Ambient (often none — **root cause**) |
| `gantri.bridge.group_chat` | `Gantri.Agents` | `GroupChatOrchestrator.cs:43` | Ambient |
| `gantri.hooks.pipeline` | `Gantri.Hooks` | `HookPipeline.cs:40` | Ambient |
| `gantri.plugins.resolve` | `Gantri.Plugins` | `PluginRouter.cs:35` | Ambient |
| `gantri.plugins.native.load` | `Gantri.Plugins.Native` | `NativePluginLoader.cs:28` | Ambient |
| `gantri.plugins.wasm.load` | `Gantri.Plugins.Wasm` | `WasmPluginLoader.cs:34` | Ambient |
| `gantri.mcp.get_tools` | `Gantri.Mcp` | `McpClientManager.cs:49` | Ambient |
| `gantri.mcp.tool_call` | `Gantri.Mcp` | `McpClientManager.cs:77` | Ambient |
| `gantri.workflows.execute` | `Gantri.Workflows` | `WorkflowEngine.cs:40` | Ambient |
| `gantri.workflows.step` | `Gantri.Workflows` | `StepExecutor.cs:30` | Ambient |
| `gantri.scheduling.run` | `Gantri.Scheduling` | `JobRunner.cs:41` | Ambient |
| `gantri.scheduling.workflow` | `Gantri.Scheduling` | `TickerJobFunctions.cs:59` | Ambient |
| `gantri.scheduling.agent` | `Gantri.Scheduling` | `TickerJobFunctions.cs:90` | Ambient |
| `gantri.scheduling.plugin` | `Gantri.Scheduling` | `TickerJobFunctions.cs:120` | Ambient |
| `chat` | `Gantri.Agents` | M.E.AI `.UseOpenTelemetry()` | Ambient |
| `invoke_agent` | `Gantri.Agents` | M.Agents.AI `.UseOpenTelemetry()` | Ambient |
| `execute_tool` | `Gantri.Agents` | M.Agents.AI `.UseOpenTelemetry()` | Ambient |

All spans use **ambient parenting** (`Activity.Current`). None pass an explicit `ActivityContext` parent. This works when a parent span is already active in the async context, but fails when there is no ambient parent — which is the case for every top-level entry point.

---

## 2. Compliance Audit: Microsoft Agent Framework Observability Docs

Before addressing trace context, we must ensure Gantri is correctly wired into the Microsoft Agent Framework and M.E.AI telemetry pipeline. Cross-referencing with the [Microsoft Agent Framework Observability Guide](https://learn.microsoft.com/en-us/agent-framework/user-guide/observability) and the [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/) reveals several gaps in the current implementation.

### 2.1 Activity Source Registration — Wildcard Mismatch

**Current (incorrect):**
```csharp
// TelemetryServiceExtensions.cs
tracing.AddSource("Microsoft.Extensions.AI");
tracing.AddSource("Microsoft.Agents.AI");
```

**Microsoft docs recommend:**
```csharp
.AddSource("*Microsoft.Extensions.AI")      // wildcard — matches any source containing this string
.AddSource("*Microsoft.Extensions.Agents*")  // wildcard — note "Agents" not "Agents.AI"
```

The `*` prefix in `AddSource()` enables wildcard matching in the OpenTelemetry .NET SDK. The actual source names emitted by the Microsoft libraries may be prefixed (e.g. `Experimental.Microsoft.Extensions.AI`) or use a different suffix (e.g. `Microsoft.Extensions.Agents` rather than `Microsoft.Agents.AI`). Exact-match registration silently drops spans from sources that don't match, which means **Gantri may be missing spans from the Agent Framework entirely** without any error.

**Fix:** Change to wildcard patterns in `TelemetryServiceExtensions.cs`.

### 2.2 Meter Registration — Wildcard Mismatch

**Current (incorrect):**
```csharp
// TelemetryServiceExtensions.cs
metrics.AddMeter("Microsoft.Agents.AI");
```

**Microsoft docs recommend:**
```csharp
.AddMeter("*Microsoft.Agents.AI")  // wildcard
```

Same wildcard issue as sources. The Agent Framework emits these standard metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `gen_ai.client.operation.duration` | Histogram (seconds) | Duration of each model operation |
| `gen_ai.client.token.usage` | Histogram (tokens) | Token usage per operation |
| `agent_framework.function.invocation.duration` | Histogram (seconds) | Duration of tool function executions |

Gantri's custom meters (`gantri.ai.completions.duration`, `gantri.ai.tokens.total`) are **separate** from these — they measure what Gantri records manually. The Agent Framework's meters capture what the framework itself observes internally. Both should be collected.

**Fix:** Add wildcard pattern and register the `agent_framework` meter.

### 2.3 GenAI Semantic Conventions — Attribute Alignment

The [OpenTelemetry GenAI Agent Span Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-agent-spans/) define standard attributes that the Microsoft Agent Framework emits automatically when `.UseOpenTelemetry()` is called:

| Convention Attribute | Gantri Custom Attribute | Status |
|---------------------|------------------------|--------|
| `gen_ai.agent.name` | `gantri.agent.name` | **Duplicate** — AF emits the standard one; Gantri adds its own |
| `gen_ai.agent.id` | — | **Missing** — Gantri doesn't set this |
| `gen_ai.conversation.id` | — | **Missing** — This is the official convention for conversation correlation |
| `gen_ai.operation.name` | — | Set by AF automatically (`invoke_agent`, `chat`, `execute_tool`) |
| `gen_ai.usage.input_tokens` | `gantri.ai.tokens.input` | **Duplicate** — AF emits the standard one |
| `gen_ai.usage.output_tokens` | `gantri.ai.tokens.output` | **Duplicate** — AF emits the standard one |
| `gen_ai.request.model` | — | Set by AF automatically |
| `gen_ai.response.model` | — | Set by AF automatically |
| `gen_ai.system` | — | Set by AF automatically (e.g. `openai`, `azure_openai`) |
| `gen_ai.provider.name` | `gantri.agent.provider` | **Duplicate** |

**Implications:**

1. Gantri's custom `gantri.*` attributes are additive — they provide Gantri-specific context that the standard conventions don't cover (session ID, message index, plugin-specific tags). They should be **kept**.
2. The standard `gen_ai.*` attributes are emitted by the AF's `.UseOpenTelemetry()` middleware automatically — Gantri doesn't need to set them manually.
3. For conversation correlation, we should set **both** `gen_ai.conversation.id` (standard) and `gantri.agent.conversation_id` (Gantri-specific) so the traces are queryable using either convention in any telemetry backend.

### 2.4 Sensitive Data Configuration

The Microsoft docs warn:

> When you enable observability for your chat clients and agents, you might see duplicated information, especially when sensitive data is enabled. The chat context (including prompts and responses) that is captured by both the chat client and the agent will be included in both spans.

**Current Gantri wiring:**
```csharp
// GantriAgentFactory.cs
.UseOpenTelemetry(loggerFactory: _loggerFactory, sourceName: "Gantri.Agents")
// ↑ No sensitive data configuration — defaults to disabled
```

**Microsoft docs recommend configuring it explicitly:**
```csharp
.UseOpenTelemetry(sourceName: "Gantri.Agents", configure: cfg => cfg.EnableSensitiveData = enableSensitiveData)
```

This should be **configurable via `telemetry.yaml`** so it can be enabled in development (for debugging prompts/responses in Aspire) and disabled in production.

### 2.5 Agent-Level vs Chat-Client-Level Telemetry

The Microsoft docs note that enabling `.UseOpenTelemetry()` on **both** the `IChatClient` and the `AIAgent` produces duplicate spans — the same prompt/response appears in both the `chat` span and the `invoke_agent` span.

**Current Gantri wiring (GantriAgentFactory.cs):**
```csharp
// Level 1: Chat client telemetry
var instrumentedClient = hookedClient.AsBuilder()
    .Use(inner => new RetryingChatClient(...))
    .UseOpenTelemetry(loggerFactory: _loggerFactory, sourceName: "Gantri.Agents")  // chat spans
    .UseLogging(_loggerFactory)
    .Build();

// Level 2: Agent telemetry
return agent.AsBuilder().UseOpenTelemetry(sourceName: "Gantri.Agents").Build();   // invoke_agent spans
```

This is **intentionally dual-instrumented** and correct for our use case — we want both the agent-level view (`invoke_agent` with tool calls) and the chat-level view (`chat` with token usage). The Microsoft docs simply advise awareness of the duplication. No change needed here, but we should document this decision.

### 2.6 API Style: `.WithOpenTelemetry()` vs `.AsBuilder().UseOpenTelemetry()`

The Microsoft docs show:
```csharp
agent.WithOpenTelemetry(sourceName: "MyApplication", enableSensitiveData: true);
```

Gantri uses:
```csharp
agent.AsBuilder().UseOpenTelemetry(sourceName: "Gantri.Agents").Build();
```

These are equivalent — `.WithOpenTelemetry()` is a convenience extension that wraps the builder pattern internally. No change needed, but if the AF ships updates, the extension method is preferred for readability.

---

## 3. Design Goals

1. **Single trace per conversation** — all messages within one agent session share a common trace ID in the CLI host
2. **Per-request trace linking in API** — each AG-UI HTTP request is its own trace (unavoidable with HTTP), but spans within a request form a proper hierarchy, and a custom `gantri.agent.session_id` attribute links related traces across turns
3. **Group chat coherence** — all agent runs within a group chat nest under the `gantri.bridge.group_chat` span (this already mostly works but needs verification)
4. **Scheduler trace roots** — scheduled jobs already create root spans (`gantri.scheduling.*`), and child operations nest correctly under them (working, no change needed)
5. **Microsoft Agent Framework compliance** — correct source/meter registration so all AF-emitted spans and metrics are captured; align with GenAI semantic conventions
6. **Configurable sensitive data** — allow enabling prompt/response capture in development via `telemetry.yaml`
7. **No breaking changes** — existing telemetry attributes, metric names, and span names remain the same; custom `gantri.*` attributes are additive alongside standard `gen_ai.*` ones
8. **Minimal surface area** — fix the parenting and compliance gaps, don't restructure the entire telemetry system

---

## 4. Why This Approach

### 3.1 Conversation-Scoped Root Span (CLI)

The CLI `AgentRunCommand` runs an interactive loop where the user sends multiple messages. Today each message is an orphaned trace. The fix is to create a single `gantri.agent.conversation` activity that wraps the entire session lifecycle. All child spans (session creation, message sends, model calls, tool executions, hooks) automatically parent under it because `Activity.Current` flows through `async/await` via `AsyncLocal<T>`.

This is the standard OpenTelemetry pattern — a long-lived root span representing the logical operation, with child spans representing individual steps.

### 3.2 Explicit Parent Context on AfAgentSession (API)

The API host cannot have a conversation-scoped root span because each HTTP request is independent. Instead, `AfAgentSession` captures the `ActivityContext` from its creation span and uses it as an explicit parent for subsequent `SendMessageAsync` calls. This ensures that even if `Activity.Current` is null when a new HTTP request arrives, the message span links back to the session's trace.

For cross-request correlation, we rely on the `gantri.agent.session_id` attribute that is already set on spans — telemetry backends can group by this attribute to reconstruct a conversation timeline.

### 3.3 Not Selected: W3C Trace Context Propagation via AG-UI

An alternative would be to propagate W3C `traceparent` headers through the AG-UI protocol so the frontend client carries the trace context across requests. This was rejected because:

- It requires changes to the AG-UI protocol or custom headers
- The frontend client (CopilotKit, etc.) would need to be modified
- It couples trace context to the transport protocol
- The session ID correlation approach achieves the same observability goal without protocol changes

---

## 5. Implementation Steps

### Step 1: Fix Activity Source and Meter Registration

**File:** `src/Core/Gantri.Telemetry/TelemetryServiceExtensions.cs`

**Current (incorrect):**
```csharp
tracing.AddSource("Microsoft.Extensions.AI");
tracing.AddSource("Microsoft.Agents.AI");

// ...

metrics.AddMeter("Microsoft.Agents.AI");
```

**Fixed:**
```csharp
// Use wildcard patterns per Microsoft Agent Framework docs
// https://learn.microsoft.com/en-us/agent-framework/user-guide/observability
tracing.AddSource("*Microsoft.Extensions.AI");      // M.E.AI chat client spans (chat, token usage)
tracing.AddSource("*Microsoft.Extensions.Agents*");  // Agent Framework spans (invoke_agent, execute_tool)

// ...

metrics.AddMeter("*Microsoft.Agents.AI");                      // AF metrics (gen_ai.client.*)
metrics.AddMeter("*agent_framework*");                          // AF function invocation metrics
```

**Why:** The Microsoft Agent Framework may emit spans/metrics from activity sources with prefixes like `Experimental.` or different suffixes than what Gantri currently registers. Wildcard patterns ensure we capture all AF telemetry regardless of internal naming changes across AF versions. Without this fix, spans from the AF's `.UseOpenTelemetry()` middleware may be silently dropped.

---

### Step 2: Add Sensitive Data Configuration

**File:** `src/Core/Gantri.Abstractions/Configuration/TelemetryOptions.cs`

Add a new property to the trace options:

```csharp
public class TraceOptions
{
    public string Exporter { get; set; } = "otlp";
    public string? Endpoint { get; set; }
    public bool EnableSensitiveData { get; set; } = false;  // NEW
}
```

**File:** `src/Integration/Gantri.Bridge/GantriAgentFactory.cs`

Pass the sensitive data flag through to `.UseOpenTelemetry()`:

```csharp
// Constructor: add TelemetryOptions parameter
private readonly bool _enableSensitiveData;

// In CreateAgentAsync:
.UseOpenTelemetry(
    loggerFactory: _loggerFactory,
    sourceName: "Gantri.Agents",
    configure: cfg => cfg.EnableSensitiveData = _enableSensitiveData)

// Agent-level:
return agent.AsBuilder()
    .UseOpenTelemetry(
        sourceName: "Gantri.Agents",
        enableSensitiveData: _enableSensitiveData)
    .Build();
```

**File:** `config/telemetry.yaml` (example update)

```yaml
telemetry:
  enabled: true
  traces:
    exporter: otlp
    enable_sensitive_data: false  # Set true in dev to capture prompts/responses in Aspire
```

**Why:** The Microsoft docs explicitly recommend configuring sensitive data capture. In development, seeing the actual prompts and responses in the trace waterfall is invaluable for debugging agent behavior. In production, it must be disabled to avoid logging PII.

---

### Step 3: Add New Semantic Conventions

**File:** `src/Core/Gantri.Telemetry/GantriSemanticConventions.cs`

Add conversation-level attributes and GenAI convention aliases:

```csharp
// Conversation attributes (new — Gantri-specific)
public const string AgentConversationId = "gantri.agent.conversation_id";
public const string AgentMessageIndex = "gantri.agent.message_index";

// GenAI semantic convention aliases (standard — set alongside Gantri attributes)
// These align with the OpenTelemetry GenAI Agent Span Conventions
// https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-agent-spans/
public const string GenAiConversationId = "gen_ai.conversation.id";
public const string GenAiAgentName = "gen_ai.agent.name";
public const string GenAiAgentId = "gen_ai.agent.id";
public const string GenAiOperationName = "gen_ai.operation.name";
```

**Why:** Setting both `gen_ai.conversation.id` (standard) and `gantri.agent.conversation_id` (custom) ensures traces are queryable in any telemetry backend — those that understand GenAI conventions will use the standard attribute, while Gantri-specific dashboards can use the custom one. The `gen_ai.agent.*` aliases are provided as constants for consistency when Gantri manually sets them on custom spans.

---

### Step 4: Create Conversation-Scoped Root Span in CLI

**File:** `src/Hosts/Gantri.Cli/Commands/AgentRunCommand.cs`

Wrap the entire session lifecycle in a root activity:

```csharp
public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    try
    {
        // Root span for the entire conversation — all child operations nest under this
        using var conversationActivity = GantriActivitySources.Agents.StartActivity(
            "gantri.agent.conversation");
        var conversationId = Guid.NewGuid().ToString("N")[..12];

        // Set both Gantri-specific and GenAI standard attributes
        conversationActivity?.SetTag(GantriSemanticConventions.AgentName, settings.AgentName);
        conversationActivity?.SetTag(GantriSemanticConventions.AgentConversationId, conversationId);
        conversationActivity?.SetTag(GantriSemanticConventions.GenAiConversationId, conversationId);
        conversationActivity?.SetTag(GantriSemanticConventions.GenAiAgentName, settings.AgentName);

        await using var session = await _orchestrator.CreateSessionAsync(settings.AgentName);
        conversationActivity?.SetTag(GantriSemanticConventions.AgentSessionId, session.SessionId);

        // ... rest of the method stays the same, but add message index tracking:
        var messageIndex = 0;

        // In the interactive loop, before each SendMessageAsync:
        // Activity.Current is the conversationActivity, so child spans auto-parent
        // The gantri.agent.session span in AfAgentSession.SendMessageAsync
        // becomes a child of gantri.agent.conversation
```

**What changes:**
- Add `using System.Diagnostics;` and `using Gantri.Telemetry;` imports
- Inject nothing new — `GantriActivitySources` is a static class
- Wrap `ExecuteAsync` body in a `using var conversationActivity` block
- Add a `messageIndex` counter, increment per message

**Expected trace structure (CLI, interactive):**

```
gantri.agent.conversation (root — lives for entire session)
├── gantri.agent.create_session (AfAgentOrchestrator)
│   ├── gantri.bridge.create_agent (GantriAgentFactory)
│   ├── gantri.agent.create_session (AfAgentSession.CreateAsync)
│   └── gantri.hooks.pipeline (session-start hook)
├── gantri.agent.session (message 1)
│   └── invoke_agent (M.Agents.AI)
│       ├── chat (M.E.AI — model call)
│       │   └── gantri.hooks.pipeline (model-call before/after)
│       ├── execute_tool (M.Agents.AI — tool call)
│       │   ├── gantri.plugins.resolve
│       │   └── gantri.mcp.tool_call
│       └── chat (M.E.AI — follow-up model call)
├── gantri.agent.session (message 2)
│   └── invoke_agent
│       └── chat
└── gantri.agent.session (message 3)
    └── ...
```

---

### Step 5: Add Conversation Context to AfAgentSession

**File:** `src/Integration/Gantri.Bridge/AfAgentSession.cs`

Store a conversation ID and message counter on the session, and propagate the parent context for API scenarios:

```csharp
public sealed class AfAgentSession : IAgentSession
{
    private readonly AIAgent _agent;
    private readonly AgentSession _session;
    private readonly ILogger _logger;
    private readonly ActivityContext _parentContext;  // NEW
    private int _messageIndex;                        // NEW

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public string AgentName { get; }
    public string ConversationId { get; }             // NEW

    private AfAgentSession(
        AIAgent agent, AgentSession session, string agentName,
        ILogger logger, ActivityContext parentContext)  // parentContext NEW
    {
        _agent = agent;
        _session = session;
        _logger = logger;
        _parentContext = parentContext;
        AgentName = agentName;
        ConversationId = Guid.NewGuid().ToString("N")[..12];
    }
```

In `CreateAsync`, capture the current activity context:

```csharp
public static async Task<AfAgentSession> CreateAsync(
    AIAgent agent, string agentName, ILogger logger,
    CancellationToken cancellationToken = default)
{
    using var activity = GantriActivitySources.Agents.StartActivity("gantri.agent.create_session");
    activity?.SetTag(GantriSemanticConventions.AgentName, agentName);

    // Capture the parent context so SendMessageAsync can re-parent under it
    // In CLI: this is the gantri.agent.conversation span
    // In API: this is the HTTP request span (or none)
    var parentContext = Activity.Current?.Context ?? default;

    var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
    var afSession = new AfAgentSession(agent, session, agentName, logger, parentContext);

    activity?.SetTag(GantriSemanticConventions.AgentSessionId, afSession.SessionId);
    activity?.SetTag(GantriSemanticConventions.AgentConversationId, afSession.ConversationId);

    return afSession;
}
```

In `SendMessageAsync` and `SendMessageStreamingAsync`, use the captured parent context:

```csharp
public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
{
    var msgIndex = Interlocked.Increment(ref _messageIndex);

    // Use explicit parent context instead of relying on ambient Activity.Current
    // This ensures the span parents correctly even in API scenarios where
    // Activity.Current may be a different HTTP request span
    using var activity = GantriActivitySources.Agents.StartActivity(
        "gantri.agent.session",
        ActivityKind.Internal,
        _parentContext);  // explicit parent

    // Gantri-specific attributes
    activity?.SetTag(GantriSemanticConventions.AgentName, AgentName);
    activity?.SetTag(GantriSemanticConventions.AgentSessionId, SessionId);
    activity?.SetTag(GantriSemanticConventions.AgentConversationId, ConversationId);
    activity?.SetTag(GantriSemanticConventions.AgentMessageIndex, msgIndex);

    // GenAI standard attributes (for cross-tool compatibility)
    activity?.SetTag(GantriSemanticConventions.GenAiConversationId, ConversationId);
    activity?.SetTag(GantriSemanticConventions.GenAiAgentName, AgentName);

    // ... rest unchanged
}
```

Apply the same pattern to `SendMessageStreamingAsync`.

**Why explicit parent context:** In the CLI, `Activity.Current` is the conversation root span, so ambient parenting would work. But in the API, `Activity.Current` is the HTTP request span for the *current* request — which is different for each turn. By capturing the parent context at session creation time, we ensure all message spans share a common ancestor regardless of the host.

---

### Step 6: Update IAgentSession Interface

**File:** `src/Core/Gantri.Abstractions/Agents/IAgentSession.cs`

Add `ConversationId` to the interface so hosts can access it:

```csharp
public interface IAgentSession : IAsyncDisposable
{
    string SessionId { get; }
    string AgentName { get; }
    string ConversationId { get; }  // NEW

    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> SendMessageStreamingAsync(string message, CancellationToken cancellationToken = default);
}
```

**Impact:** Any other `IAgentSession` implementations will need to add this property. Check for other implementations and update them.

---

### Step 7: Add Conversation Metrics

**File:** `src/Core/Gantri.Telemetry/GantriMeters.cs`

Add a histogram for conversation duration and a counter for messages per conversation:

```csharp
// Conversation metrics (new)
public static readonly Histogram<double> AgentConversationDuration =
    Meter.CreateHistogram<double>("gantri.agent.conversation.duration", "ms",
        "End-to-end conversation duration");

public static readonly Counter<long> AgentMessagesTotal =
    Meter.CreateCounter<long>("gantri.agent.messages.total", "{messages}",
        "Total messages sent across all conversations");
```

Record `AgentMessagesTotal` in `AfAgentSession.SendMessageAsync`. Record `AgentConversationDuration` when the conversation activity ends (in `AgentRunCommand` after the loop exits, or in `AfAgentSession.DisposeAsync`).

---

### Step 8: Fix Group Chat Trace Hierarchy

**File:** `src/Integration/Gantri.Bridge/GroupChatOrchestrator.cs`

The group chat orchestrator already creates a `gantri.bridge.group_chat` root span. Child operations should nest under it via `Activity.Current`. However, `agent.CreateSessionAsync()` and `agent.RunAsync()` are called on `AIAgent` directly (not through `AfAgentSession`), so they bypass the session-level spans.

Verify that the Microsoft Agent Framework's `.UseOpenTelemetry()` spans (`invoke_agent`, `chat`, `execute_tool`) correctly inherit the parent context. If they don't, wrap each agent run in an explicit child span:

```csharp
foreach (var (name, agent) in agents)
{
    using var agentActivity = GantriActivitySources.Agents.StartActivity(
        "gantri.group_chat.agent_turn");
    agentActivity?.SetTag(GantriSemanticConventions.AgentName, name);
    agentActivity?.SetTag("gantri.group_chat.iteration", iteration + 1);

    var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
    var response = await agent.RunAsync(currentMessage, session, cancellationToken: cancellationToken);
    // ...
}
```

**Expected trace structure (group chat):**

```
gantri.bridge.group_chat (root)
├── gantri.bridge.create_agent (agent-1)
├── gantri.bridge.create_agent (agent-2)
├── gantri.group_chat.agent_turn (agent-1, iteration 1)
│   └── invoke_agent
│       ├── chat (model call)
│       └── execute_tool (if any)
├── gantri.group_chat.agent_turn (agent-2, iteration 1)
│   └── invoke_agent
│       └── chat (model call)
└── gantri.hooks.pipeline (group-chat end hook)
```

---

### Step 9: Fix Scheduler Agent Job Parenting

**File:** `src/Domain/Gantri.Scheduling/TickerJobFunctions.cs`

The agent job function (`ExecuteAgentJobAsync`) creates a `gantri.scheduling.agent` span and then calls `_agentOrchestrator.CreateSessionAsync()` + `session.SendMessageAsync()`. The `gantri.scheduling.agent` span should already serve as the parent for all child operations via `Activity.Current`. Verify this works correctly — no code changes expected, just validation.

**Expected trace structure (scheduled agent job):**

```
gantri.scheduling.run (JobRunner)
└── gantri.scheduling.agent (TickerJobFunctions)
    ├── gantri.agent.create_session (AfAgentOrchestrator)
    │   ├── gantri.bridge.create_agent (GantriAgentFactory)
    │   └── gantri.agent.create_session (AfAgentSession)
    └── gantri.agent.session (message)
        └── invoke_agent
            └── chat
```

---

### Step 10: Add Baggage Propagation for Cross-Span Correlation

To ensure `ConversationId` and `SessionId` are available on all descendant spans (including those created by Microsoft libraries), propagate them as `Activity` baggage:

**File:** `src/Integration/Gantri.Bridge/AfAgentSession.cs`

In `SendMessageAsync`, after starting the activity:

```csharp
activity?.SetBaggage(GantriSemanticConventions.AgentConversationId, ConversationId);
activity?.SetBaggage(GantriSemanticConventions.AgentSessionId, SessionId);
```

This ensures that even spans created by `.UseOpenTelemetry()` in M.E.AI and M.Agents.AI will inherit the baggage, making it possible to filter/group by conversation ID in the telemetry backend even if those spans don't explicitly set the tag.

---

## 6. Files Changed Summary

| File | Change Type | Description |
|------|-------------|-------------|
| `src/Core/Gantri.Telemetry/TelemetryServiceExtensions.cs` | Modified | Fix wildcard source/meter registration (Step 1) |
| `src/Core/Gantri.Telemetry/GantriSemanticConventions.cs` | Modified | Add conversation + GenAI convention constants (Step 3) |
| `src/Core/Gantri.Telemetry/GantriMeters.cs` | Modified | Add conversation duration histogram, messages counter (Step 7) |
| `src/Core/Gantri.Abstractions/Configuration/TelemetryOptions.cs` | Modified | Add `EnableSensitiveData` to `TraceOptions` (Step 2) |
| `src/Core/Gantri.Abstractions/Agents/IAgentSession.cs` | Modified | Add `ConversationId` property (Step 6) |
| `src/Integration/Gantri.Bridge/GantriAgentFactory.cs` | Modified | Pass sensitive data flag to `.UseOpenTelemetry()` (Step 2) |
| `src/Integration/Gantri.Bridge/AfAgentSession.cs` | Modified | Store parent context, add conversation ID, explicit parenting, GenAI attrs (Step 5) |
| `src/Integration/Gantri.Bridge/GroupChatOrchestrator.cs` | Modified | Add per-agent-turn child spans (Step 8) |
| `src/Hosts/Gantri.Cli/Commands/AgentRunCommand.cs` | Modified | Add conversation root span, message index tracking (Step 4) |
| `config/telemetry.yaml` | Modified | Add `enable_sensitive_data` option (Step 2) |

---

## 7. Files NOT Changed (and Why)

| File | Reason |
|------|--------|
| `GantriActivitySources.cs` | No new `ActivitySource` needed — all new spans use existing `Gantri.Agents` source |
| `HookPipeline.cs` | Already creates `gantri.hooks.pipeline` span — parents correctly via ambient context |
| `PluginActionFunction.cs` | No span creation — plugin execution spans come from `PluginRouter` |
| `TickerJobFunctions.cs` | Already has root spans — child operations should parent correctly (verify only) |
| `Gantri.Api/Program.cs` | ASP.NET Core provides HTTP request spans automatically — no additional root span needed |
| `AfAgentOrchestrator.cs` | Already creates `gantri.agent.create_session` — parents correctly under conversation root |

---

## 8. Verification Checklist

### 8.1 CLI Verification

Run an interactive agent session and verify in Aspire/Jaeger:

- [ ] All messages within one session share a single trace ID
- [ ] `gantri.agent.conversation` is the root span
- [ ] `gantri.agent.create_session` is a child of the conversation span
- [ ] Each `gantri.agent.session` (per message) is a child of the conversation span
- [ ] `invoke_agent` → `chat` → `execute_tool` hierarchy is intact within each message
- [ ] `gantri.hooks.pipeline` spans are children of their triggering operation
- [ ] `gantri.agent.conversation_id` attribute is set on conversation and session spans
- [ ] `gantri.agent.message_index` increments correctly (1, 2, 3, ...)
- [ ] Conversation root span duration equals the total session time

### 8.2 API Verification

Run multiple turns via the AG-UI endpoint and verify:

- [ ] Each HTTP request produces its own trace (expected — one trace per request)
- [ ] Within each trace, spans form a proper hierarchy under the HTTP server span
- [ ] All traces for the same session share the same `gantri.agent.session_id` value
- [ ] All traces for the same session share the same `gantri.agent.conversation_id` value
- [ ] Filtering by `gantri.agent.conversation_id` in the telemetry backend shows all related traces

### 8.3 Group Chat Verification

Run a group chat and verify:

- [ ] `gantri.bridge.group_chat` is the root span
- [ ] All agent creation spans are children
- [ ] `gantri.group_chat.agent_turn` spans nest under the group chat root
- [ ] `invoke_agent` / `chat` spans nest under the agent turn span
- [ ] Hook pipeline spans nest correctly

### 8.4 Scheduler Verification

Trigger a scheduled agent job and verify:

- [ ] `gantri.scheduling.run` is the root span
- [ ] `gantri.scheduling.agent` is a child
- [ ] Session creation and message spans nest under the scheduling span

### 8.5 Microsoft Agent Framework Compliance Verification

Verify the wildcard source/meter registration captures AF telemetry:

- [ ] `invoke_agent` spans appear in the trace (from M.Agents.AI `.UseOpenTelemetry()`)
- [ ] `chat` spans appear with `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens` attributes
- [ ] `execute_tool` spans appear for tool calls with `gen_ai.operation.name` = `execute_tool`
- [ ] `gen_ai.client.operation.duration` metric is collected (check Aspire Metrics tab)
- [ ] `gen_ai.client.token.usage` metric is collected
- [ ] `gen_ai.conversation.id` attribute is set on conversation and session spans
- [ ] When `enable_sensitive_data: true`, prompt/response content appears in span attributes
- [ ] When `enable_sensitive_data: false` (default), no prompt/response content in spans

---

## 9. Data Flow: CLI Agent Conversation

```
User types message
        │
        ▼
AgentRunCommand.ExecuteAsync
  ┌─ Activity: gantri.agent.conversation (ROOT) ──────────────────────┐
  │                                                                     │
  │  _orchestrator.CreateSessionAsync(agentName)                       │
  │    ┌─ Activity: gantri.agent.create_session ──────────────────┐   │
  │    │  _agentFactory.CreateAgentAsync(definition)              │   │
  │    │    ┌─ Activity: gantri.bridge.create_agent ────────┐     │   │
  │    │    │  builds IChatClient, collects tools            │     │   │
  │    │    └────────────────────────────────────────────────┘     │   │
  │    │  AfAgentSession.CreateAsync(agent)                       │   │
  │    │    captures parentContext = Activity.Current.Context      │   │
  │    └──────────────────────────────────────────────────────────┘   │
  │                                                                     │
  │  LOOP: for each user message                                       │
  │    session.SendMessageAsync(message)                               │
  │      ┌─ Activity: gantri.agent.session (parent=parentContext) ─┐  │
  │      │  _agent.RunAsync(message, _session)                     │  │
  │      │    ┌─ Activity: invoke_agent (M.Agents.AI) ───────┐    │  │
  │      │    │  ┌─ Activity: chat (M.E.AI) ──────────┐      │    │  │
  │      │    │  │  hooks: model-call before/after     │      │    │  │
  │      │    │  │  HTTP call to LLM provider          │      │    │  │
  │      │    │  └─────────────────────────────────────┘      │    │  │
  │      │    │  ┌─ Activity: execute_tool (M.Agents.AI)─┐    │    │  │
  │      │    │  │  plugin/MCP tool execution            │    │    │  │
  │      │    │  └───────────────────────────────────────┘    │    │  │
  │      │    │  ┌─ Activity: chat (follow-up) ──────────┐   │    │  │
  │      │    │  │  model processes tool results         │   │    │  │
  │      │    │  └───────────────────────────────────────┘   │    │  │
  │      │    └──────────────────────────────────────────────┘    │  │
  │      └─────────────────────────────────────────────────────────┘  │
  │  END LOOP                                                          │
  │                                                                     │
  └─────────────────────────────────────────────────────────────────────┘
```

---

## 10. Testing Strategy

### Unit Tests

Add tests to verify trace parenting in `Gantri.Bridge.Tests`:

1. **ConversationRootParenting** — Create an `AfAgentSession`, call `SendMessageAsync` twice, assert both message spans share the same parent trace ID.
2. **ExplicitParentContext** — Start a custom activity, create a session within it, verify the session captures that activity's context as parent.
3. **MessageIndexIncrement** — Call `SendMessageAsync` three times, verify `gantri.agent.message_index` tags are 1, 2, 3.

Use `ActivityListener` to collect emitted activities in tests:

```csharp
var activities = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "Gantri.Agents",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => activities.Add(activity)
};
ActivitySource.AddActivityListener(listener);
```

### Integration Tests

1. **CLI end-to-end** — Run `gantri agent run <name> --input "test"` and verify OTLP export contains a single trace with `gantri.agent.conversation` as root.
2. **API end-to-end** — Send two AG-UI requests, verify both traces share the same `gantri.agent.conversation_id` attribute.
3. **Group chat** — Run a group chat, verify all spans nest under `gantri.bridge.group_chat`.

---

## 11. Naming Convention Summary

| Span Name | Purpose | Activity Source |
|-----------|---------|----------------|
| `gantri.agent.conversation` | **NEW** — Root span for a full agent session | `Gantri.Agents` |
| `gantri.group_chat.agent_turn` | **NEW** — Per-agent turn within group chat | `Gantri.Agents` |
| `gantri.agent.create_session` | Existing — Session creation | `Gantri.Agents` |
| `gantri.bridge.create_agent` | Existing — Agent factory build | `Gantri.Agents` |
| `gantri.agent.session` | Existing — Per-message execution | `Gantri.Agents` |
| `gantri.agent.session.streaming` | Existing — Per-message streaming | `Gantri.Agents` |

| Attribute | Convention | Purpose | New/Existing |
|-----------|-----------|---------|--------------|
| `gantri.agent.conversation_id` | Gantri | Correlates all spans in a conversation | **NEW** |
| `gen_ai.conversation.id` | GenAI Standard | Same value as above — standard convention | **NEW** |
| `gantri.agent.message_index` | Gantri | Message position within conversation | **NEW** |
| `gen_ai.agent.name` | GenAI Standard | Set by AF automatically; also set by Gantri on custom spans | **NEW** (alias) |
| `gen_ai.agent.id` | GenAI Standard | Unique agent identifier | **NEW** (alias) |
| `gantri.agent.session_id` | Gantri | Existing session identifier | Existing |
| `gantri.agent.name` | Gantri | Agent name (Gantri-specific) | Existing |

| Metric | Source | Purpose | New/Existing |
|--------|--------|---------|--------------|
| `gantri.agent.conversation.duration` | Gantri | Conversation duration histogram | **NEW** |
| `gantri.agent.messages.total` | Gantri | Messages counter | **NEW** |
| `gen_ai.client.operation.duration` | AF (auto) | Model call duration | **Newly captured** (Step 1 fix) |
| `gen_ai.client.token.usage` | AF (auto) | Token usage per operation | **Newly captured** (Step 1 fix) |
| `agent_framework.function.invocation.duration` | AF (auto) | Tool function execution duration | **Newly captured** (Step 1 fix) |

---

## 12. Implementation Order

The steps should be implemented in this order due to dependencies:

1. **Step 1** (Source/Meter wildcards) — no dependencies, fixes silent span/metric loss
2. **Step 2** (Sensitive data config) — no dependencies, pure additive
3. **Step 3** (Semantic Conventions) — no dependencies, pure additive
4. **Step 7** (Metrics) — no dependencies, pure additive
5. **Step 6** (IAgentSession interface) — required before AfAgentSession changes
6. **Step 5** (AfAgentSession) — depends on Steps 3, 6
7. **Step 4** (AgentRunCommand) — depends on Step 3
8. **Step 8** (GroupChatOrchestrator) — depends on Step 3
9. **Step 9** (Scheduler verification) — no code changes, just testing
10. **Step 10** (Baggage propagation) — depends on Step 5
11. **Verification** — run all checklist items from Section 8

---

## 13. References

- [Microsoft Agent Framework — Observability Guide](https://learn.microsoft.com/en-us/agent-framework/user-guide/observability)
- [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- [OpenTelemetry GenAI Agent Span Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-agent-spans/)
- [.NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [Agent Framework OTel Sample (C#)](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/AgentOpenTelemetry)
