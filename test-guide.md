# Daily News Pipeline — Real-World Test & Validation Guide

End-to-end walkthrough for running the full news pipeline with live services: Brave Search MCP, Azure OpenAI, file-save plugin, file-glob plugin, and the scheduled workflow.

## Prerequisites

| Requirement | Why |
|---|---|
| .NET 10 SDK | Build and run Gantri |
| Node.js 18+ with npm/npx | Brave Search MCP server runs via `npx` |
| Brave Search API key | [Get one free at brave.com/search/api](https://brave.com/search/api/) |
| Azure OpenAI endpoint + key | The `news-summarizer` agent calls `gpt-5-mini` via Azure Foundry |

## Step 0 — Set Environment Variables

The pipeline needs two secrets. Set them in your shell before running anything.

**PowerShell:**
```powershell
$env:BRAVE_API_KEY = "your-brave-api-key-here"
$env:AZURE_OPENAI_API_KEY = "your-azure-openai-key-here"
```

**Bash / Git Bash:**
```bash
export BRAVE_API_KEY="your-brave-api-key-here"
export AZURE_OPENAI_API_KEY="your-azure-openai-key-here"
```

Verify they're set:
```bash
echo $BRAVE_API_KEY        # should print your key, not empty
echo $AZURE_OPENAI_API_KEY # should print your key, not empty
```

## Step 1 — Build

```bash
dotnet build Gantri.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

If the build fails, check you're on .NET 10 SDK (`dotnet --version`).

## Step 2 — Verify Configuration Is Loaded

Run each CLI command from the repo root and check the expected output.

### 2a. Plugins discovered

```bash
dotnet run --project src/Hosts/Gantri.Cli -- plugin list
```

You should see a table with at least these three plugins:

| Name | Type | Actions |
|---|---|---|
| hello-world | native | hello |
| file-save | native | save |
| file-glob | native | search |

### 2b. Agents registered

```bash
dotnet run --project src/Hosts/Gantri.Cli -- agent list
```

Should list `code-reviewer`, `ticket-triager`, and **`news-summarizer`**.

### 2c. Workflows registered

```bash
dotnet run --project src/Hosts/Gantri.Cli -- workflow list
```

Should list **`daily-news`**.

### 2d. Schedule registered

```bash
dotnet run --project src/Hosts/Gantri.Cli -- schedule list
```

Should list **`daily-news-9pm`** with cron `0 21 * * *` and type `workflow`.

### 2e. Full config dump

```bash
dotnet run --project src/Hosts/Gantri.Cli -- config show
```

Scroll through and confirm the `mcp` section shows:
```
mcp:
  servers:
    brave:
      command: npx
      args: ["-y", "@brave/brave-search-mcp-server"]
```

## Step 3 — Test the Brave Search MCP Server Standalone

Before running the full pipeline, verify the MCP server starts and responds.

```bash
npx -y @brave/brave-search-mcp-server
```

This should start the MCP server process on stdio. It will hang waiting for JSON-RPC input — that's correct. Press `Ctrl+C` to kill it. If npx fails, run `npm install -g @brave/brave-search-mcp-server` first.

## Step 4 — Run the Agent Directly (Single-Shot)

This is the fastest way to validate the live pipeline. It runs the `news-summarizer` agent with a single input message, hitting Brave Search and Azure OpenAI for real.

```bash
dotnet run --project src/Hosts/Gantri.Cli -- agent run news-summarizer \
  --input "Fetch today's top news stories, format as markdown, and save to disk."
```

### What to watch for

1. **MCP connection** — the CLI spawns `npx @brave/brave-search-mcp-server` as a child process. You should not see connection errors.
2. **Tool calls** — the agent should call `brave.brave_news_search` (fetching real news), then `file-save:save` (writing the markdown file).
3. **Output** — the agent returns the formatted markdown to the console. Look for:
   - A `# Daily News Summary - YYYY-MM-DD` heading
   - Multiple `## Story Title` sections
   - Source names and URLs for each story
4. **Saved file** — check that the file was written:

```bash
ls data/news/
cat data/news/news-$(date +%Y-%m-%d).md
```

The file should contain the same markdown the agent printed.

### If it fails

| Symptom | Likely cause |
|---|---|
| `Provider 'azure-openai' is missing api_key` | `AZURE_OPENAI_API_KEY` env var not set |
| `MCP server 'brave' is not connected` | `BRAVE_API_KEY` not set, or npx not on PATH |
| `Provider 'azure-openai' not found` | Config not loading — run `config show` first |
| Timeout / no response | Network issue reaching Azure or Brave APIs |
| `npx: command not found` | Node.js not installed or not on PATH |

## Step 5 — Run the Workflow

This tests the workflow engine layer — it creates a workflow execution, routes to the `fetch-and-summarize` step, which creates an agent session for `news-summarizer`.

```bash
dotnet run --project src/Hosts/Gantri.Cli -- workflow run daily-news
```

### What to watch for

Same as Step 4, plus:
- The output should show workflow execution metadata (duration, step outputs).
- The `fetch-and-summarize` step output should contain the markdown.

## Step 6 — Validate with file-glob

After Step 4 or 5, you should have at least one file in `data/news/`. Use the `file-glob` plugin to search it.

You can test this through an agent that has `file-glob` access, or by writing a quick script. The easiest way is to add `file-glob` to an agent's plugins and ask it to search. But for a direct validation, create a throwaway test file:

```bash
# Confirm files exist
ls data/news/*.md

# Manually verify content search works by checking the plugin test suite:
dotnet test tests/Gantri.Plugins.Native.Tests --filter "FileGlob"
```

Those 5 tests exercise the real `SearchAction` class against real temp files on disk (not mocked).

## Step 7 — Run the Scheduled Workflow via Worker

This validates the full scheduling stack: TickerQ cron job triggers the workflow at 9 PM daily.

### 7a. Start the Worker

```bash
dotnet run --project src/Hosts/Gantri.Worker
```

The Worker will:
1. Load config including `scheduling.yaml`
2. Seed the `daily-news-9pm` cron job into TickerQ's SQLite store
3. Start the MCP server for CLI remote management
4. Begin the TickerQ scheduler loop

Leave it running. At 9 PM local time (or whenever `0 21 * * *` next fires), the Worker will:
1. Execute `gantri_workflow_job` with payload `{ workflow: "daily-news" }`
2. Run the `daily-news` workflow
3. The `news-summarizer` agent fetches news, formats markdown, saves to `data/news/`

### 7b. Manually trigger the job (don't wait until 9 PM)

In a second terminal, use the CLI to trigger the job immediately:

```bash
dotnet run --project src/Hosts/Gantri.Cli -- worker jobs trigger daily-news-9pm
```

Then check the Worker terminal for execution logs, and verify the output file:

```bash
cat data/news/news-$(date +%Y-%m-%d).md
```

### 7c. Check job status

```bash
dotnet run --project src/Hosts/Gantri.Cli -- worker status
dotnet run --project src/Hosts/Gantri.Cli -- worker jobs list
```

## Step 8 — Run the Same Day Again

Run the agent or workflow a second time on the same day:

```bash
dotnet run --project src/Hosts/Gantri.Cli -- agent run news-summarizer \
  --input "Fetch today's top news stories, format as markdown, and save to disk."
```

Verify that `data/news/news-YYYY-MM-DD.md` is **overwritten** with fresh content (the file-save plugin uses `File.WriteAllTextAsync` which overwrites).

## Validation Checklist

After running through the steps above, confirm each item:

- [ ] `dotnet build Gantri.slnx` — 0 errors, 0 warnings
- [ ] `plugin list` shows `file-save` and `file-glob`
- [ ] `agent list` shows `news-summarizer`
- [ ] `workflow list` shows `daily-news`
- [ ] `schedule list` shows `daily-news-9pm` at `0 21 * * *`
- [ ] `config show` includes `mcp.servers.brave`
- [ ] `agent run news-summarizer --input "..."` produces markdown with real news headlines
- [ ] `data/news/news-YYYY-MM-DD.md` exists with the same markdown content
- [ ] `workflow run daily-news` completes successfully with step output
- [ ] Worker starts without errors and seeds the cron job
- [ ] `worker jobs trigger daily-news-9pm` executes the pipeline from the Worker
- [ ] `dotnet test Gantri.slnx` — all 173+ tests pass

## Architecture: What Happens End-to-End

```
scheduling.yaml                 config/mcp.yaml
  daily-news-9pm                  brave -> npx @brave/brave-search-mcp-server
  cron: 0 21 * * *
        |
        v
  TickerQ (Worker)
  gantri_workflow_job
        |
        v
  WorkflowEngine.ExecuteAsync("daily-news")
        |
        v
  AgentStepHandler -> AgentOrchestrator.CreateSession("news-summarizer")
        |
        v
  AgentSession.RunAgentLoopAsync()
    |
    |-- Turn 1: LLM returns FunctionCallContent("brave.brave_news_search")
    |            ToolExecutor -> McpClientManager -> StdioMcpServer -> Brave API
    |            result: JSON news data
    |
    |-- Turn 2: LLM returns FunctionCallContent("file-save:save")
    |            ToolExecutor -> PluginRouter -> SaveAction
    |            result: "Saved 2847 characters to data/news/news-2026-02-27.md"
    |
    |-- Turn 3: LLM returns text (formatted markdown)
    |            No tool calls -> loop exits
        |
        v
  WorkflowResult { Success: true, FinalOutput: "# Daily News Summary..." }
```
