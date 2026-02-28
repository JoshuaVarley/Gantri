using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Workflows;
using Gantri.Cli.Infrastructure;
using Gantri.Cli.Interactive;
using Gantri.Cli.Interactive.Commands;

namespace Gantri.Integration.Tests;

public class SlashCommandRouterTests
{
    [Theory]
    [InlineData("/agent news", "agent", new[] { "news" })]
    [InlineData("/workflow list", "workflow", new[] { "list" })]
    [InlineData("/help", "help", new string[0])]
    [InlineData("/groupchat a,b hello world", "groupchat", new[] { "a,b", "hello", "world" })]
    [InlineData("/exit", "exit", new string[0])]
    public void Parse_ExtractsNameAndArgs(string input, string expectedName, string[] expectedArgs)
    {
        var (name, args) = SlashCommandRouter.Parse(input);

        name.Should().Be(expectedName);
        args.Should().BeEquivalentTo(expectedArgs);
    }

    [Fact]
    public void Parse_EmptySlash_ReturnsEmpty()
    {
        var (name, args) = SlashCommandRouter.Parse("/");

        name.Should().BeEmpty();
        args.Should().BeEmpty();
    }

    [Fact]
    public void Register_And_TryGetCommand_WorksCorrectly()
    {
        var router = new SlashCommandRouter();
        var exitCmd = new ExitCommand();
        var clearCmd = new ClearCommand();

        router.Register(exitCmd);
        router.Register(clearCmd);

        router.TryGetCommand("exit", out var cmd).Should().BeTrue();
        cmd.Should().BeSameAs(exitCmd);

        router.TryGetCommand("clear", out var cmd2).Should().BeTrue();
        cmd2.Should().BeSameAs(clearCmd);

        router.TryGetCommand("unknown", out var cmd3).Should().BeFalse();
    }

    [Fact]
    public void Commands_ReturnsAllRegistered()
    {
        var router = new SlashCommandRouter();
        router.Register(new ExitCommand());
        router.Register(new ClearCommand());

        router.Commands.Should().HaveCount(2);
        router.Commands.Should().ContainKey("exit");
        router.Commands.Should().ContainKey("clear");
    }
}

public class ConsoleContextTests
{
    [Fact]
    public async Task EndSessionAsync_DisposesAndClearsSession()
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var workflowEngine = Substitute.For<IWorkflowEngine>();
        var workerClient = new WorkerMcpClient(new WorkerOptions());
        var renderer = new ConsoleRenderer();

        var context = new ConsoleContext(orchestrator, workflowEngine, workerClient, renderer);

        var session = Substitute.For<IAgentSession>();
        context.ActiveSession = session;
        context.ActiveAgentName = "test-agent";
        context.MessageCount = 5;

        await context.EndSessionAsync();

        context.ActiveSession.Should().BeNull();
        context.ActiveAgentName.Should().BeNull();
        context.MessageCount.Should().Be(0);
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task EndSessionAsync_WhenNoSession_DoesNothing()
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var workflowEngine = Substitute.For<IWorkflowEngine>();
        var workerClient = new WorkerMcpClient(new WorkerOptions());
        var renderer = new ConsoleRenderer();

        var context = new ConsoleContext(orchestrator, workflowEngine, workerClient, renderer);

        // Should not throw
        await context.EndSessionAsync();
        context.ActiveSession.Should().BeNull();
    }
}

public class ExitCommandTests
{
    [Fact]
    public async Task ExecuteAsync_SetsExitRequested()
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var workflowEngine = Substitute.For<IWorkflowEngine>();
        var workerClient = new WorkerMcpClient(new WorkerOptions());
        var renderer = new ConsoleRenderer();
        var context = new ConsoleContext(orchestrator, workflowEngine, workerClient, renderer);
        var cmd = new ExitCommand();

        context.ExitRequested.Should().BeFalse();

        await cmd.ExecuteAsync([], context, CancellationToken.None);

        context.ExitRequested.Should().BeTrue();
    }
}

public class ToolApprovalResultTests
{
    [Fact]
    public void Approve_ReturnsApprovedResult()
    {
        var result = ToolApprovalResult.Approve();
        result.Approved.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Reject_ReturnsRejectedResult()
    {
        var result = ToolApprovalResult.Reject("Not safe");
        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("Not safe");
    }
}

public class AutoApproveToolHandlerTests
{
    [Fact]
    public async Task RequestApprovalAsync_AlwaysApproves()
    {
        var handler = new AutoApproveToolHandler();
        var result = await handler.RequestApprovalAsync("any-tool",
            new Dictionary<string, object?> { ["key"] = "value" });

        result.Approved.Should().BeTrue();
    }
}
