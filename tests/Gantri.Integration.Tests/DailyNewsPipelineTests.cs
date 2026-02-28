using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.Configuration;
using Gantri.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Integration.Tests;

public class DailyNewsPipelineTests
{
    private static IHookPipeline CreatePassthroughPipeline()
    {
        var pipeline = Substitute.For<IHookPipeline>();
        pipeline.ExecuteAsync(
            Arg.Any<HookEvent>(),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = new HookContext(callInfo.ArgAt<HookEvent>(0));
                return new ValueTask<HookContext>(ctx);
            });
        return pipeline;
    }

    [Fact]
    public async Task DailyNewsWorkflow_ExecutesFullPipeline()
    {
        // Arrange — define the workflow
        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["daily-news"] = new()
            {
                Name = "daily-news",
                Description = "Fetches and saves a daily news summary",
                Trigger = "scheduled",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "fetch-and-summarize",
                        Type = "agent",
                        Agent = "news-summarizer",
                        Input = "Fetch today's top news stories, format as markdown, and save to disk."
                    }
                ]
            }
        };

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var expectedPath = $"data/news/news-{today}.md";
        var expectedMarkdown = $"""
            # Daily News Summary - {today}

            ## Breaking: Major Tech Announcement
            **Source:** TechNews | [Link](https://example.com/tech)
            A major technology company has announced a groundbreaking product today.

            ---

            ## Global Climate Summit Reaches Agreement
            **Source:** WorldNews | [Link](https://example.com/climate)
            World leaders have reached a historic agreement on climate change.
            """;

        // Mock orchestrator — the agent session simulates the agentic tool-call loop result
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var mockSession = Substitute.For<IAgentSession>();
        mockSession.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expectedMarkdown);
        mockSession.AgentName.Returns("news-summarizer");
        mockSession.SessionId.Returns("test-session");

        orchestrator.CreateSessionAsync("news-summarizer", Arg.Any<CancellationToken>())
            .Returns(mockSession);

        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();

        // Build workflow engine with all step handlers
        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();

        var stepExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var parallelHandler = new ParallelStepHandler(() => stepExecutor);

        var fullExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, parallelHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows),
            fullExecutor,
            pipeline,
            NullLogger<WorkflowEngine>.Instance);

        // Act
        var result = await engine.ExecuteAsync("daily-news");

        // Assert — workflow completed successfully
        result.Success.Should().BeTrue();
        result.FinalOutput.Should().NotBeNullOrEmpty();
        result.FinalOutput.Should().Contain("# Daily News Summary");
        result.FinalOutput.Should().Contain("## ");
        result.StepOutputs.Should().ContainKey("fetch-and-summarize");

        // Verify the agent was invoked
        await mockSession.Received(1).SendMessageAsync(
            Arg.Is<string>(s => s.Contains("Fetch today's top news")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ScheduledJobDefinition_DailyNews_ConfiguredCorrectly()
    {
        // Arrange — simulate the scheduling config
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["daily-news-9pm"] = new()
            {
                Type = "workflow",
                Workflow = "daily-news",
                Cron = "0 21 * * *",
                Enabled = true
            }
        };

        // Assert — verify config structure
        var job = jobs["daily-news-9pm"];
        job.Type.Should().Be("workflow");
        job.Workflow.Should().Be("daily-news");
        job.Cron.Should().Be("0 21 * * *");
        job.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task FileGlobPlugin_SearchesSavedNewsFiles()
    {
        // Arrange — create temp directory with test markdown files
        var tempDir = Path.Combine(Path.GetTempPath(), $"gantri-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "news-2025-01-01.md"),
                "# Daily News Summary - 2025-01-01\n## Tech Story\nA tech story.\n");

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "news-2025-01-02.md"),
                "# Daily News Summary - 2025-01-02\n## Climate Story\nA climate story.\n");

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "notes.txt"),
                "This is not a markdown file.\n");

            // Act — use SearchAction directly
            var action = new FileGlob.Plugin.SearchAction();

            // Test 1: Glob pattern filters correctly (*.md excludes .txt)
            var globResult = await action.ExecuteAsync(new Gantri.Plugins.Sdk.ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = tempDir,
                    ["pattern"] = "*.md"
                }
            });

            globResult.Success.Should().BeTrue();
            var globOutput = globResult.Output as string;
            globOutput.Should().NotBeNull();
            globOutput.Should().Contain("news-2025-01-01.md");
            globOutput.Should().Contain("news-2025-01-02.md");
            globOutput.Should().NotContain("notes.txt");

            // Test 2: Search with matching term returns file:line: content format
            var searchResult = await action.ExecuteAsync(new Gantri.Plugins.Sdk.ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = tempDir,
                    ["pattern"] = "*.md",
                    ["search_term"] = "Climate"
                }
            });

            searchResult.Success.Should().BeTrue();
            var searchOutput = searchResult.Output as string;
            searchOutput.Should().NotBeNull();
            searchOutput.Should().Contain("Climate Story");
            searchOutput.Should().Contain(":2:");
            searchOutput.Should().NotContain("Tech Story");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
