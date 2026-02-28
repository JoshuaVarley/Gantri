# Gantri Architecture

This document covers the technical architecture, internals, and reference documentation for Gantri. For a project overview and getting started guide, see the [README](README.md).

## Layered Architecture

Gantri follows a strict layered architecture with top-down dependencies only. The **Integration** layer sits between Domain and Hosts, providing the bridge between Gantri's systems and the Microsoft Agent Framework.

```
Layer 4 ─ Hosts          Gantri.Cli    Gantri.Worker
                              │              │
Layer 3 ─ Domain         Gantri.Agents  Gantri.Workflows  Gantri.Scheduling
                              │              │
Layer 2 ─ Integration    Gantri.Bridge
                              │
Layer 1 ─ Core           Hooks  Plugins  AI  MCP  Configuration  Telemetry
                              │
Layer 0 ─ Contracts      Gantri.Abstractions
```

### Projects

| Project | Layer | Purpose |
|---------|-------|---------|
| `Gantri.Abstractions` | 0 | All interfaces, DTOs, events, enums. Zero external dependencies beyond `Microsoft.Extensions.AI.Abstractions`. |
| `Gantri.Telemetry` | 1 | `ActivitySource` and `Meter` definitions for every subsystem. |
| `Gantri.Hooks` | 1 | Event-driven hook pipeline with pattern matching and priority ordering. |
| `Gantri.Plugins.Sdk` | 1 | Shared authoring interfaces for plugin developers (`ISdkPluginAction`, `ISdkPluginHook`). |
| `Gantri.Plugins.Native` | 1 | Loads .NET plugins into isolated `AssemblyLoadContext` instances with reflection bridging. |
| `Gantri.Plugins` | 1 | Unified `PluginRouter` that discovers and routes to registered loaders by manifest type. |
| `Gantri.AI` | 1 | `ModelProviderRegistry` for model alias resolution across providers. |
| `Gantri.Mcp` | 1 | `McpClientManager` for connecting to MCP servers. `McpPermissionManager` for per-agent access control. |
| `Gantri.Configuration` | 1 | YAML loader with `${ENV_VAR}` substitution and import resolution. |
| `Gantri.Plugins.Wasm` | 1 | WASM plugin loader via Wasmtime. Sandboxed execution of `.wasm` modules with shared-memory JSON protocol. |
| **`Gantri.Bridge`** | **2** | **Adapts Gantri's plugin/hook/MCP systems to Microsoft Agent Framework.** Contains `GantriAgentFactory`, `AfAgentSession`, `AfAgentOrchestrator`, `AfWorkflowEngine`, `GroupChatOrchestrator`, `PluginActionFunction`, `McpToolFunction`, and `HookMiddleware`. |
| `Gantri.Agents` | 3 | `ToolExecutor` (unified tool routing). Delegates agent orchestration to `Gantri.Bridge`. |
| `Gantri.Workflows` | 3 | `WorkflowEngine` executes multi-step workflows. `StepExecutor` routes to agent, plugin, condition, approval, and parallel handlers. `WorkflowContext` resolves `${input.*}`, `${steps.*}`, `${env.*}` templates. |
| `Gantri.Scheduling` | 3 | `JobScheduler` backed by TickerQ cron/time tickers. `JobRunner` supports direct workflow/agent/plugin execution paths. |
| `Gantri.Cli` | 4 | Spectre.Console CLI host with all user-facing commands and interactive console REPL. Dual-mode entry: no args launches the interactive console with slash commands, streaming, and human-in-the-loop approval; with args runs traditional CLI subcommands. |
| `Gantri.Worker` | 4 | Background worker host that runs TickerQ processing plus Gantri scheduling orchestration for unattended execution. |

## The Bridge Layer

`Gantri.Bridge` is the integration layer that connects Gantri's unique capabilities to the Microsoft Agent Framework:

| Component | Purpose |
|-----------|---------|
| `GantriAgentFactory` | Builds AF `AIAgent` instances from Gantri `AgentDefinition`. Resolves models via `ModelProviderRegistry`, creates `IChatClient`, applies hook middleware, collects plugin + MCP tools, enforces `allowed_actions` and MCP server permissions, and calls `.AsAIAgent()`. |
| `PluginActionFunction` | Real `AIFunction` subclass that wraps `IPluginRouter`. AF calls `InvokeCoreAsync` directly to execute plugin actions. Supports optional `IToolApprovalHandler` for human-in-the-loop approval. |
| `McpToolFunction` | Real `AIFunction` that wraps `IMcpToolProvider.InvokeToolAsync` for MCP server tools. Supports optional `IToolApprovalHandler` for human-in-the-loop approval. |
| `HookMiddleware` | `IChatClient` middleware (`.Use()` pattern) that fires `agent:{name}:model-call:before/after` hooks around every model call, including streaming calls. |
| `AfAgentSession` | Implements `IAgentSession` by delegating to AF's `AIAgent.RunAsync` (and `RunStreamingAsync` for streaming) and `AgentSession`. |
| `AfAgentOrchestrator` | Implements `IAgentOrchestrator` using `GantriAgentFactory`. Drop-in replacement for the old orchestrator. |
| `AfWorkflowEngine` | Implements `IWorkflowEngine` with dual routing: simple sequential agent workflows execute via AF agents, complex workflows delegate to the legacy engine (`ILegacyWorkflowEngine`). |
| `GroupChatOrchestrator` | Runs multiple AF agents sequentially in a group chat, passing each output as the next agent's input. Fires `orchestration:group-chat:start/end` hooks. |
| `InvokeGantriPluginAction` | Custom action type for AF declarative workflows that calls through to `IPluginRouter`. |

## Agent Execution Flow

When you run an agent, the following happens:

1. Load `AgentDefinition` from YAML configuration
2. `GantriAgentFactory` builds an AF `AIAgent`:
   - Creates an `IChatClient` for the configured model/provider
   - Wraps the client with `HookMiddleware` to fire before/after hooks on every model call
   - Collects `PluginActionFunction` tools from configured plugins
   - Collects `McpToolFunction` tools from configured MCP servers
   - Calls `chatClient.AsAIAgent(instructions, name, tools)` to produce the AF agent
3. `AfAgentSession.CreateAsync()` creates an AF `AgentSession` via `agent.CreateSessionAsync()`
4. User messages are sent via `agent.RunAsync(message, session)` (or `RunStreamingAsync` for token-by-token streaming) which handles the full tool-calling loop internally:
   - AF calls the model with the system prompt, conversation history, and tool definitions
   - If the model returns tool calls, AF invokes `PluginActionFunction.InvokeCoreAsync` or `McpToolFunction.InvokeCoreAsync` directly
   - If an `IToolApprovalHandler` is registered (interactive mode), the user is prompted to approve/reject each tool call before execution
   - Tool results are appended and the model is called again
   - Continues until the model returns a text response

### AI Provider Wiring

The CLI composition root (`Program.cs`) wires Azure OpenAI as follows:

1. **Config loading** — `gantri.yaml` is parsed into `GantriConfigRoot`
2. **Agent definitions** — `config.Agents` dictionary registered into DI
3. **Model registry** — Each provider and its models registered in `ModelProviderRegistry`
4. **Client factory** — A `Func<string, AiModelOptions, IChatClient>` creates `AzureOpenAIClient` instances using the provider's `endpoint` + `api_key`, then calls `.GetChatClient(deploymentName).AsIChatClient()` to bridge to `Microsoft.Extensions.AI`
5. **Bridge registration** — `AddGantriBridge()` registers `GantriAgentFactory`, `AfAgentOrchestrator` (as `IAgentOrchestrator`), and all bridge adapters
6. **Plugin discovery** — `PluginRouter.ScanPluginDirectories()` initializes plugin scanning from configured directories

The core `Gantri.AI` project remains provider-agnostic — Azure-specific packages (`Azure.AI.OpenAI`) live only in the host projects.

## CLI Commands

### Agent Commands

**Run an agent** with a single input:

```bash
gantri agent run news-summarizer --input "Fetch today's top news stories"
```

**Run an agent interactively** (starts a conversation loop):

```bash
gantri agent run code-reviewer
```

**List all configured agents:**

```bash
gantri agent list
```

### Workflow Commands

**Run a workflow** with optional input:

```bash
gantri workflow run daily-news
```

**Run a content review workflow:**

```bash
gantri workflow run content-review --input "Write about AI agents"
```

**List all configured workflows:**

```bash
gantri workflow list
```

### Orchestration Commands

**Run a group chat** with multiple agents:

```bash
gantri orchestrate group-chat content-writer,fact-checker,editor --input "Write about AI agents" --max-iterations 5
```

This runs the agents sequentially: the content-writer drafts, the fact-checker reviews, and the editor polishes. Each agent's output becomes the next agent's input.

### Schedule Commands

**List all scheduled jobs** with next run times:

```bash
gantri schedule list
```

### Worker Commands

The `worker` commands communicate with a running Gantri Worker instance via MCP to manage scheduled jobs remotely.

**Check worker health:**

```bash
gantri worker status
```

**List scheduled jobs from the worker:**

```bash
gantri worker jobs list
```

**Manually trigger a scheduled job:**

```bash
gantri worker jobs trigger nightly-review
```

### Plugin Commands

**List discovered plugins:**

```bash
gantri plugin list
```

### Config Commands

**Show current configuration** as a formatted tree:

```bash
gantri config show
```

**Validate a configuration file:**

```bash
gantri config validate
```

Validation checks YAML syntax, merged imports (`framework.imports`), provider configuration (endpoint/base_url, API key, model definitions), agent-to-provider cross-references, and plugin directory existence.

## Interactive Console

Launch the interactive console by running `gantri` with no arguments. This provides a persistent REPL where you can chat with agents, run workflows, and manage sessions — all without restarting the CLI.

### Slash Commands

| Command | Description |
|---------|-------------|
| `/agent <name>` | Start an interactive session with a named agent |
| `/agent list` | List all configured agents |
| `/workflow <name>` | Run a workflow by name |
| `/workflow list` | List all configured workflows |
| `/workflow status [id]` | Show workflow execution status |
| `/groupchat <a,b,c> <input>` | Run a group chat with multiple agents |
| `/schedule` | List scheduled jobs from the worker |
| `/schedule trigger <name>` | Manually trigger a scheduled job |
| `/schedule pause <name>` | Pause a scheduled job |
| `/schedule resume <name>` | Resume a paused scheduled job |
| `/schedule detail <name>` | Show detailed info about a scheduled job |
| `/approve [id]` | Approve a pending workflow execution |
| `/tools` | Show info about the current agent's tools |
| `/session` | Show current session info (agent, ID, message count) |
| `/clear` | Clear the console |
| `/help` | Show available commands |
| `/exit` | Exit the console |

### Agent Sessions with Streaming

Start a session with `/agent <name>`. The prompt changes to show the active agent, and all text input is sent as messages with streaming token output:

```
gantri> /agent news-summarizer
Session started with 'news-summarizer' (id: a3f7b2c1d4e5)

news-summarizer> Fetch today's top AI news
Assistant:
Here are today's top AI stories...

news-summarizer> /exit
```

### Human-in-the-Loop Tool Approval

In interactive mode, when an agent calls a plugin or MCP tool, the console displays the tool name and parameters in a table and prompts for approval before execution:

```
┌─────────────────────────────────────────┐
│       Tool Call: brave.brave_news_search│
├───────────┬─────────────────────────────┤
│ Parameter │ Value                       │
├───────────┼─────────────────────────────┤
│ query     │ top AI news today           │
│ count     │ 5                           │
└───────────┴─────────────────────────────┘
Allow this tool call?
> Approve
  Reject
  Always approve this tool
```

Selecting "Always approve this tool" skips the prompt for subsequent calls to the same tool within the session. Non-interactive mode (CLI with arguments, Worker) auto-approves all tool calls.

### Inline Workflow Approval

When a workflow reaches an approval step in interactive mode, it prompts inline instead of persisting to disk and requiring a separate `resume` command:

```
gantri> /workflow content-review
Running workflow 'content-review'...
┌──────────────────────────────────┐
│  Workflow Approval Required      │
│  Review draft before publishing  │
└──────────────────────────────────┘
Do you approve this step?
> Approve
  Reject
  View step outputs

Workflow completed in 3.2s
```

### Dual-Mode Entry

- **`gantri`** (no arguments) — launches the interactive console
- **`gantri <command> [args]`** — runs the traditional CLI subcommand and exits

Both modes share the same DI-registered services. The interactive console additionally registers `InteractiveToolApprovalHandler` (for tool approval prompts) and `InteractiveApprovalStepHandler` (for inline workflow approval).

## End-to-End Scenarios

Gantri includes three realistic end-to-end test scenarios that exercise the full stack. These tests use mock `IChatClient` instances (no live API calls required) and validate the complete flow through AF agents, plugins, MCP tools, hooks, and workflows.

### Running the E2E Tests

```bash
# Run all tests
dotnet test Gantri.slnx

# Run only integration tests
dotnet test tests/Gantri.Integration.Tests

# Run a specific scenario
dotnet test tests/Gantri.Integration.Tests --filter "FullyQualifiedName~AfDailyNewsPipelineTests"
dotnet test tests/Gantri.Integration.Tests --filter "FullyQualifiedName~AfWorkflowApprovalTests"
dotnet test tests/Gantri.Integration.Tests --filter "FullyQualifiedName~GroupChatContentReviewTests"
```

### Scenario 1: Daily News Pipeline via AF Agent

**Test:** `AfDailyNewsPipelineTests`

Tests the `news-summarizer` agent executing through AF's `AIAgent` with Brave MCP tools and file-save plugin tools.

**What it validates:**
- `GantriAgentFactory` correctly creates an AF `AIAgent` from a Gantri `AgentDefinition`
- Plugin tools (`file-save.save`) are registered as real `PluginActionFunction` instances
- MCP tools (`brave.brave_news_search`) are registered as real `McpToolFunction` instances
- `AfAgentSession` wraps the AF agent and delegates `SendMessageAsync` to `agent.RunAsync`
- Hook middleware fires `agent:*:model-call:before` and `agent:*:model-call:after` events around model calls

### Scenario 2: Multi-Step Workflow with Approval Gate

**Test:** `AfWorkflowApprovalTests`

Tests a 3-step workflow: agent -> approval -> agent. The workflow pauses at the approval gate, persists state to disk, and resumes from the checkpoint.

**What it validates:**
- `WorkflowEngine` correctly executes sequential steps
- `AgentStepHandler` delegates to `IAgentOrchestrator` for agent steps
- `ApprovalStepHandler` pauses execution and persists state via `WorkflowStateManager`
- `WorkflowStateManager` writes state files to disk with `waiting_approval` status
- Resume from checkpoint re-executes remaining steps
- Step outputs are preserved across pause/resume boundaries

### Scenario 3: Group Chat Content Review Pipeline

**Test:** `GroupChatContentReviewTests`

Three agents (content-writer, fact-checker, editor) collaborate sequentially in a group chat. Tests multi-agent orchestration with hook monitoring.

**What it validates:**
- `GroupChatOrchestrator` runs agents in the correct order
- Each agent receives the previous agent's output as input
- AF's `AIAgent` passes instructions via `ChatOptions.Instructions` (not system messages)
- Hook events fire for all agent model calls
- Group chat lifecycle hooks (`orchestration:group-chat:start/end`) fire correctly
- Unknown agent names produce clear error messages

### Running Scenarios Against a Live API

To run the agents against a real Azure OpenAI endpoint (not the mock tests), configure your environment and use the CLI:

```bash
# Set your Azure OpenAI credentials
export AZURE_OPENAI_API_KEY="your-api-key"

# Scenario 1: News summarizer with MCP tools
gantri agent run news-summarizer --input "Fetch today's top news stories"

# Scenario 2: Content review workflow with approval gate
gantri workflow run content-review --input "Write about the future of AI agents"

# Scenario 3: Group chat with three agents
gantri orchestrate group-chat content-writer,fact-checker,editor --input "Write about AI agents"
```

## Agentic Coding

Gantri includes end-to-end agentic coding capabilities — AI agents that can plan, implement, test, review, and debug code autonomously. Each coding agent has access to a curated set of file, shell, and git plugins, and operates within the framework's security model.

### Running Coding Workflows

Use the pre-built workflows for common coding tasks:

```bash
# Full implementation workflow: plan -> implement -> test -> review
gantri workflow run code-implement --input "Add a health check endpoint"

# Debug an issue
gantri workflow run code-debug --input "Tests failing in UserService.cs"

# Review uncommitted changes
gantri workflow run code-review

# Analyze and improve test coverage
gantri workflow run code-test
```

### Running Individual Agents

Run a specific coding agent directly:

```bash
# Plan an implementation
gantri agent run coding-planner --input "Plan adding pagination to the API"

# Review current changes
gantri agent run coding-reviewer
```

### Security

Coding agents operate under the same layered security model as all Gantri agents:

- **Shell command allowlisting** — Agents with `shell-exec` define `allowed_commands` patterns that restrict which commands can be executed (e.g., `dotnet build*`, `dotnet test*`).
- **File operation confinement** — File plugins confine all operations to the agent's `working_directory`.
- **Tool approval** — In interactive mode, each tool call is presented for user approval before execution.

## Configuration Reference

All configuration lives in YAML files. The CLI auto-discovers `config/gantri.yaml`, `config/gantri.yml`, `gantri.yaml`, or `gantri.yml` in the working directory.

### Environment Variables

Use `${VAR_NAME}` syntax in any YAML value to substitute environment variables at load time:

```yaml
ai:
  providers:
    azure-openai:
      api_key: ${AZURE_OPENAI_API_KEY}
```

### Full Configuration Reference

```yaml
# Framework settings
framework:
  name: Gantri
  version: 0.1.0
  log_level: Information       # Minimum log level
  data_dir: ./data             # Working data directory
  imports: []                  # Additional YAML files to merge

# AI provider configuration
ai:
  default_model: gpt-5-mini   # Default model alias
  providers:
    azure-openai:
      endpoint: https://your-resource.openai.azure.com/  # Azure OpenAI endpoint
      api_key: ${AZURE_OPENAI_API_KEY}
      # api_version: 2024-12-01-preview  # Optional API version
      models:
        gpt-5-mini:
          id: gpt-5-mini                  # Model ID (also used as deployment name)
          # deployment_name: my-deploy    # Override deployment name if different from id
          description: Azure OpenAI GPT-5 Mini
          max_tokens: 8192
          default_temperature: 0.3
  rate_limits:
    azure-openai:
      requests_per_minute: 60
      tokens_per_minute: 100000

# Agent definitions
agents:
  my-agent:
    name: my-agent
    model: gpt-5-mini                    # Model alias from ai.providers.*.models
    provider: azure-openai               # Provider key from ai.providers
    temperature: 0.3                     # Override default temperature
    max_tokens: 4096                     # Override default max tokens
    system_prompt: |                     # Inline system prompt
      You are a helpful assistant.
    # system_prompt_file: prompts/my-agent.txt  # Or load from file
    skills: []                           # Skill identifiers
    plugins: []                          # Plugin names this agent can use
    mcp_servers: []                      # MCP server names this agent can access
    allowed_actions: []                  # Restrict tools by name (e.g., file-save.save, brave.search, brave.*)
    allowed_commands: []                 # Shell command patterns for shell-exec (e.g., "dotnet build*")
    working_directory: ./data/news       # Working directory for file operations (default: framework.data_dir)

# Workflow definitions
workflows:
  review-and-triage:
    name: review-and-triage
    description: Review code and triage issues
    steps:
      - id: review
        type: agent
        agent: code-reviewer
        input: "Review: ${input.text}"
      - id: triage
        type: agent
        agent: ticket-triager
        input: "Triage based on: ${steps.review.output}"
      - id: check-severity
        type: condition
        condition: "${steps.triage.output}"
      - id: parallel-notify
        type: parallel
        steps:
          - id: notify-slack
            type: plugin
            plugin: notifications
            action: send-slack
            input: "${steps.triage.output}"
          - id: notify-email
            type: plugin
            plugin: notifications
            action: send-email
            input: "${steps.triage.output}"

# Scheduling configuration
scheduling:
  jobs:
    nightly-review:
      type: workflow
      workflow: review-and-triage
      cron: "0 2 * * *"                   # Standard 5-field cron expression
      enabled: true
      input: "Nightly code review"
    hourly-triage:
      type: agent
      agent: ticket-triager
      cron: "0 * * * *"
      enabled: false

# Plugin configuration
plugins:
  dirs:                                  # Directories to scan for plugins
    - ./plugins/built-in
    - ./plugins/custom
  native_trust_dirs: []                  # Trusted directories (skip validation)
  global: []                             # Globally available plugins
  per_agent: {}                          # Per-agent plugin overrides

# Hook bindings
hooks:
  bindings:
    - event: "agent:*:model-call:before"
      plugin: my-logging-plugin
      hook: log-model-call
      priority: 100                      # Lower = runs first (default: 500)

# Telemetry configuration
telemetry:
  enabled: true
  service_name: gantri
  service_version: 1.0.0
  resource_attributes: {}                # Extra OTel resource attributes
  traces:
    exporter: console                    # console, otlp, zipkin, jaeger
    # endpoint: http://localhost:4317
    sampling:
      strategy: always_on               # always_on, always_off, trace_id_ratio
      ratio: 1.0
  metrics:
    exporter: console
    # endpoint: http://localhost:4317
    export_interval_ms: 30000
  logs:
    exporter: console
    min_level: Information
```

## Security Model

Gantri applies layered controls for tool execution. In practice, a tool call is executable only when it passes all applicable gates below.

### 1) Tool Allowlisting (`allowed_actions`)

- `agents.<name>.allowed_actions` limits which tool names are registered for that agent.
- Supported forms:
  - Exact: `file-save.save`, `brave.search`
  - Prefix wildcard: `file-save.*`, `brave.*`
- If `allowed_actions` is empty, no allowlist filter is applied.

### 2) MCP Server Permissions

- MCP access is additionally constrained by per-agent server permissions (`mcp_servers`).
- Effective MCP access is the intersection of:
  - tools permitted by `allowed_actions` (if configured), and
  - servers permitted for that agent.

### 3) Runtime Tool Approval

- Interactive CLI mode prompts the user before each plugin/MCP tool call.
- Non-interactive mode auto-approves tool calls unless a custom approval handler is registered.

### 4) File Operation Confinement

- Built-in file plugins (`file-read`, `file-save`, `file-edit`, `file-delete`, `file-glob`, `directory-list`, `project-detect`) confine paths to the agent `working_directory` when it is configured.
- Absolute or relative paths outside that directory are rejected.
- If no `working_directory` is configured, the framework falls back to `framework.data_dir`.

### 5) Shell Command Allowlisting

- Agent definitions support an `allowed_commands` property that restricts which commands `shell-exec` can execute.
- Patterns support exact matching (case-insensitive) and prefix wildcards (e.g., `dotnet build*` matches `dotnet build`, `dotnet build --configuration Release`, etc.).
- The framework injects the agent's allowlist into `shell-exec` at runtime. Commands not matching any pattern are rejected.
- If `allowed_commands` is empty or not set, all commands are allowed (backward compatible).

### Recommended Baseline

- Set `working_directory` for every agent that can access file plugins.
- Define `allowed_actions` for production agents (prefer exact names over broad wildcards).
- Define `allowed_commands` for agents with `shell-exec` to restrict executable commands.
- Keep `mcp_servers` minimal per agent and avoid sharing broad MCP access across unrelated agents.

## Workflow Engine

Workflows define multi-step execution pipelines in YAML. Each step can invoke an agent, call a plugin action, evaluate a condition, request approval, or run sub-steps in parallel.

Simple sequential agent-only workflows are automatically routed through AF agents via `AfWorkflowEngine`. Complex workflows (parallel, approval, condition, plugin steps) execute through the legacy engine (`ILegacyWorkflowEngine`).

### Step Types

| Type | Description |
|------|-------------|
| `agent` | Sends input to a named agent and captures the response |
| `plugin` | Executes a plugin action with parameters |
| `condition` | Evaluates a template expression for truthiness |
| `approval` | Pauses the workflow and persists state for human review |
| `parallel` | Executes child steps concurrently via `Task.WhenAll` |

### Variable Resolution

Templates use `${...}` syntax and resolve against three scopes:

| Pattern | Resolves To |
|---------|-------------|
| `${input.key}` | Workflow input parameter |
| `${steps.stepId.output}` | Output from a previous step |
| `${env.VAR_NAME}` | Environment variable |

Unknown variables are preserved as-is, allowing safe forward references.

## Group Chat Orchestration

The `GroupChatOrchestrator` enables multi-agent collaboration by running agents sequentially in a pipeline:

```
Input -> Agent 1 -> Output 1 -> Agent 2 -> Output 2 -> Agent 3 -> Final Output
```

Each agent receives the previous agent's output as its input. The orchestrator fires `orchestration:group-chat:start` and `orchestration:group-chat:end` hook events for monitoring.

Example with the content review pipeline:

```bash
gantri orchestrate group-chat content-writer,fact-checker,editor \
  --input "Write a comprehensive guide to AI agents" \
  --max-iterations 1
```

1. **content-writer** receives the topic and drafts markdown content (can use `file-save` plugin)
2. **fact-checker** receives the draft and reviews it for accuracy
3. **editor** receives the reviewed content and polishes it for publication

## Job Scheduling

Gantri scheduling uses TickerQ cron/time tickers to trigger jobs on a recurring basis.

### Worker Host

The `Gantri.Worker` project runs the scheduler as a background service:

```bash
dotnet run --project src/Hosts/Gantri.Worker
```

This starts TickerQ-backed processing that executes due jobs. Jobs can trigger workflows, agents, or plugins.

### Manual Triggering

Jobs can also be triggered programmatically:

```csharp
var scheduler = provider.GetRequiredService<IJobScheduler>();
await scheduler.TriggerAsync("nightly-review");
```

## Plugin System

### Built-in Plugin Parameters

**file-read.read**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | yes | File path to read |
| `offset` | integer | no | Starting line number (1-based, default: 1) |
| `limit` | integer | no | Maximum number of lines to return (default: 500) |

**file-save.save**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | yes | File path to write to |
| `content` | string | yes | Text content to save |

**file-edit.search-replace**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | yes | File path to edit |
| `search` | string | yes | Exact text to find |
| `replace` | string | yes | Replacement text |
| `occurrence` | integer | no | Which occurrence to replace (1=first, 0=all, default: 1) |

**file-edit.insert**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | yes | File path to edit |
| `line` | integer | yes | Line number to insert before |
| `content` | string | yes | Text content to insert |

**file-delete.delete**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | yes | File path to delete |

**file-glob.search**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Directory to search in (defaults to working directory) |
| `pattern` | string | no | Glob pattern to match files (default: `*.md`) |
| `search_term` | string | no | Optional text to search within matching files |

**directory-list.tree**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Directory to list (defaults to working directory) |
| `depth` | integer | no | Maximum depth to recurse (default: 3) |
| `pattern` | string | no | Glob pattern to filter files |
| `include_hidden` | boolean | no | Include hidden files/directories (default: false) |

**shell-exec.run**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `command` | string | yes | Shell command to execute |
| `timeout_seconds` | integer | no | Timeout in seconds (default: 120) |

**git-operations.status**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Repository directory (defaults to working directory) |

**git-operations.diff**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Repository directory |
| `staged` | boolean | no | Show staged changes (default: false) |
| `path` | string | no | Limit diff to a specific path |

**git-operations.log**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Repository directory |
| `count` | integer | no | Number of commits to show (default: 10) |
| `oneline` | boolean | no | Use one-line format (default: true) |

**git-operations.commit**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `message` | string | yes | Commit message |
| `paths` | array | no | Files to stage (stages all if empty) |

**project-detect.analyze**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `directory` | string | no | Directory to analyze (defaults to working directory) |

**web-fetch.fetch**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | string | yes | URL to fetch (auto-upgrades HTTP to HTTPS) |
| `max_length` | integer | no | Maximum content length in characters (default: 50000) |

### WASM Plugins

WASM plugins run in a Wasmtime sandbox, enabling language-agnostic plugin authoring. The WASM module must export:

- `memory` — Linear memory
- `allocate(size: i32) -> i32` — Buffer allocation
- `execute(action_ptr, action_len, input_ptr, input_len) -> i32` — Action execution (0 = success)
- `get_output_ptr() -> i32` / `get_output_len() -> i32` — Output retrieval

Communication uses JSON serialized through shared memory. Create a `manifest.json` with `"type": "wasm"` and set `"entry"` to the `.wasm` file.

### Writing Plugins

Plugins are .NET class libraries that reference `Gantri.Plugins.Sdk` and implement `ISdkPluginAction`.

#### 1. Create a Plugin Project

```bash
dotnet new classlib -n MyPlugin -o plugins/custom/my-plugin
cd plugins/custom/my-plugin
dotnet add reference ../../../src/Core/Gantri.Plugins.Sdk/Gantri.Plugins.Sdk.csproj
```

#### 2. Implement an Action

```csharp
using Gantri.Plugins.Sdk;

public sealed class GreetAction : ISdkPluginAction
{
    public string ActionName => "greet";
    public string Description => "Returns a personalized greeting";

    public Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var name = context.Parameters.TryGetValue("name", out var val) && val is string s
            ? s
            : "World";

        return Task.FromResult(ActionResult.Ok($"Hello, {name}!"));
    }
}
```

#### 3. Create a Manifest

Create `manifest.json` in the plugin root:

```json
{
    "name": "my-plugin",
    "version": "1.0.0",
    "type": "native",
    "description": "My custom plugin",
    "entry": "MyPlugin.dll",
    "capabilities": {
        "required": [],
        "optional": []
    },
    "exports": {
        "actions": [
            {
                "name": "greet",
                "description": "Returns a personalized greeting",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Name to greet" }
                    }
                }
            }
        ],
        "hooks": []
    }
}
```

#### 4. Register the Plugin Directory

Add the parent directory to your config:

```yaml
plugins:
  dirs:
    - ./plugins/custom
```

The plugin will be discovered automatically and available to agents as the tool `my-plugin.greet`. During development, the loader finds the DLL in the build output directory (`bin/Debug/<tfm>/`).

> **Note:** When plugins receive parameters from the agent loop, `JsonElement` values are normalized to .NET primitives (string, long, double, bool). The `val is string s` pattern in the example above works correctly with normalized values.

## Hook System

Hooks provide cross-cutting middleware for every subsystem operation. Events follow the pattern:

```
{domain}:{component}:{action}:{timing}
```

| Segment | Description | Examples |
|---------|-------------|----------|
| `domain` | Top-level subsystem | `agent`, `plugin`, `mcp`, `orchestration` |
| `component` | Specific component | `session`, `router`, `group-chat` |
| `action` | What's happening | `model-call`, `tool-use`, `session-start`, `start`, `end` |
| `timing` | When the hook fires | `before`, `after`, `onerror`, `around` |

Wildcards (`*`) are supported in each segment for pattern matching:

```yaml
hooks:
  bindings:
    # Run before every agent model call
    - event: "agent:*:model-call:before"
      plugin: my-logger
      hook: log-request
      priority: 100

    # Run after any tool use
    - event: "agent:*:tool-use:after"
      plugin: my-auditor
      hook: audit-tool-call
```

### Hook Timings

| Timing | Behavior |
|--------|----------|
| `Before` | Runs before the operation. Can cancel via `HookContext.Cancel()`. |
| `After` | Runs after successful completion. |
| `OnError` | Runs when the operation throws an exception. |
| `Around` | Wraps the operation. Controls whether and how it executes. |

Hooks within the same timing run in priority order (lower number = higher priority, default 500).

## Tool Routing

When AF's `AIAgent` receives tool calls from the model, it invokes the corresponding `AIFunction` directly:

| Tool Type | AIFunction | Routed To |
|-----------|-----------|-----------|
| Plugin tool | `PluginActionFunction` | `IPluginRouter.ResolveAsync` -> `IPlugin.ExecuteActionAsync` |
| MCP tool | `McpToolFunction` | `IMcpToolProvider.InvokeToolAsync` |

Tools are named `{source}.{tool}` (e.g., `file-save.save`, `brave.brave_news_search`) and registered with the AF agent at creation time. The AI model calls them by name, and AF handles the tool-calling loop automatically.

Both `PluginActionFunction` and `McpToolFunction` accept an optional `IToolApprovalHandler`. When registered (interactive console mode), each tool call is presented to the user for approval before execution. If rejected, the tool returns an error message to the model. In non-interactive mode (CLI with args, Worker), no handler is registered and all tool calls execute immediately.

## Telemetry

Every subsystem emits OpenTelemetry traces and metrics automatically.

### Activity Sources

Traces are grouped by subsystem:

| Source | Spans |
|--------|-------|
| `Gantri.Hooks` | Hook pipeline execution |
| `Gantri.Plugins` | Plugin loading and action execution |
| `Gantri.Plugins.Native` | Native plugin operations |
| `Gantri.Plugins.Wasm` | WASM plugin operations |
| `Gantri.AI` | Model completion calls |
| `Gantri.Mcp` | MCP server connections and tool calls |
| `Gantri.Agents` | Agent session lifecycle and tool use |
| `Gantri.Bridge` | AF agent creation, group chat orchestration |
| `Gantri.Workflows` | Workflow step execution |
| `Gantri.Scheduling` | Scheduled job runs |

### Metrics

Key instruments include:

- `gantri.agent.sessions.total` — Total agent sessions created
- `gantri.agent.sessions.active` — Currently active sessions
- `gantri.ai.completions.total` — AI completion calls (tagged by provider/model)
- `gantri.ai.completions.duration` — Completion latency histogram
- `gantri.plugin.actions.total` — Plugin action executions
- `gantri.hook.executions.total` — Hook pipeline runs
- `gantri.mcp.tool_calls.total` — MCP tool invocations
- `gantri.scheduler.jobs.total` — Scheduled job executions
- `gantri.scheduler.jobs.duration` — Job execution latency histogram

All metrics use `gantri.*` semantic conventions defined in `GantriSemanticConventions`.

## Programmatic Usage

You can use Gantri as a library without the CLI:

```csharp
using Azure;
using Azure.AI.OpenAI;
using Gantri.Abstractions.Configuration;
using Gantri.Agents;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Hooks;
using Gantri.Mcp;
using Gantri.Plugins;
using Gantri.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register all core services
services.AddLogging();
services.AddGantriTelemetry();
services.AddGantriHooks();
services.AddGantriPlugins();
services.AddGantriAI();
services.AddGantriMcp();
services.AddGantriAgents();
services.AddGantriBridge();

// Register agent definitions
services.AddSingleton(new Dictionary<string, AgentDefinition>
{
    ["my-agent"] = new AgentDefinition
    {
        Name = "my-agent",
        Model = "gpt-5-mini",
        Provider = "azure-openai",
        SystemPrompt = "You are a helpful assistant."
    }
});

// Register Azure OpenAI chat client factory
services.AddSingleton<Func<string, AiModelOptions, IChatClient>>((providerName, model) =>
{
    var client = new AzureOpenAIClient(
        new Uri("https://your-resource.openai.azure.com/"),
        new AzureKeyCredential("your-api-key"));
    return client.GetChatClient(model.DeploymentName ?? model.Id).AsIChatClient();
});

var provider = services.BuildServiceProvider();

// Populate the model registry
var registry = provider.GetRequiredService<ModelProviderRegistry>();
registry.RegisterProvider("azure-openai", new AiProviderOptions
{
    Endpoint = "https://your-resource.openai.azure.com/",
    Models = new Dictionary<string, AiModelOptions>
    {
        ["gpt-5-mini"] = new AiModelOptions { Id = "gpt-5-mini" }
    }
});

// Run a single agent
var orchestrator = provider.GetRequiredService<IAgentOrchestrator>();
await using var session = await orchestrator.CreateSessionAsync("my-agent");
var response = await session.SendMessageAsync("Hello!");
Console.WriteLine(response);

// Run a group chat
var groupResult = await orchestrator.RunGroupChatAsync(
    participants: ["content-writer", "fact-checker", "editor"],
    input: "Write about AI agents",
    maxIterations: 1);
Console.WriteLine(groupResult);
```
