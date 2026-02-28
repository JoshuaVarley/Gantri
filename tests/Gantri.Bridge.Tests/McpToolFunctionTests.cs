using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Mcp;
using Gantri.Bridge;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge.Tests;

public class McpToolFunctionTests
{
    [Fact]
    public async Task InvokeCoreAsync_CallsMcpToolProvider()
    {
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider.InvokeToolAsync("brave", "search", Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(McpToolResult.Ok("search results"));

        var function = new McpToolFunction(
            "brave", "search", "Search the web",
            null, mcpToolProvider);

        function.Name.Should().Be("brave.search");
        function.Description.Should().Be("Search the web");

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["query"] = "test"
        });

        var result = await function.InvokeAsync(args);

        result.Should().Be("search results");
        await mcpToolProvider.Received(1).InvokeToolAsync(
            "brave", "search", Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeCoreAsync_ReturnsErrorOnFailure()
    {
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider.InvokeToolAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(McpToolResult.Fail("timeout"));

        var function = new McpToolFunction(
            "server", "tool", "A tool",
            null, mcpToolProvider);

        var result = await function.InvokeAsync(new AIFunctionArguments());

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("Error");
    }

    [Fact]
    public async Task InvokeCoreAsync_WithApprovalHandler_ChecksBeforeExecution()
    {
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider.InvokeToolAsync("brave", "search", Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(McpToolResult.Ok("results"));

        var approvalHandler = Substitute.For<IToolApprovalHandler>();
        approvalHandler.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(ToolApprovalResult.Approve());

        var function = new McpToolFunction(
            "brave", "search", "Search",
            null, mcpToolProvider, approvalHandler);

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "test" }));

        result.Should().Be("results");
        await approvalHandler.Received(1).RequestApprovalAsync(
            "brave.search", Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeCoreAsync_WithRejectedApproval_ReturnsError()
    {
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();

        var approvalHandler = Substitute.For<IToolApprovalHandler>();
        approvalHandler.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(ToolApprovalResult.Reject("Denied"));

        var function = new McpToolFunction(
            "brave", "search", "Search",
            null, mcpToolProvider, approvalHandler);

        var result = await function.InvokeAsync(new AIFunctionArguments());

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("rejected");
        result!.ToString().Should().Contain("Denied");

        // MCP tool should NOT have been called
        await mcpToolProvider.DidNotReceive().InvokeToolAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>());
    }
}
