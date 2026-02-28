using System.Text.Json;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Plugins;
using Gantri.Bridge;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge.Tests;

public class PluginActionFunctionTests
{
    [Fact]
    public async Task InvokeCoreAsync_CallsPluginRouter()
    {
        var plugin = Substitute.For<IPlugin>();
        plugin.ExecuteActionAsync("greet", Arg.Any<PluginActionInput>(), Arg.Any<CancellationToken>())
            .Returns(new PluginActionResult { Success = true, Output = "Hello, World!" });

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("hello", Arg.Any<CancellationToken>())
            .Returns(plugin);

        var function = new PluginActionFunction(
            "hello", "greet", "Greets the user",
            null, pluginRouter);

        function.Name.Should().Be("hello.greet");
        function.Description.Should().Be("Greets the user");

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["name"] = "World"
        });

        var result = await function.InvokeAsync(args);

        result.Should().Be("Hello, World!");
        await pluginRouter.Received(1).ResolveAsync("hello", Arg.Any<CancellationToken>());
        await plugin.Received(1).ExecuteActionAsync("greet", Arg.Any<PluginActionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeCoreAsync_ReturnsErrorOnFailure()
    {
        var plugin = Substitute.For<IPlugin>();
        plugin.ExecuteActionAsync("fail", Arg.Any<PluginActionInput>(), Arg.Any<CancellationToken>())
            .Returns(new PluginActionResult { Success = false, Error = "Something broke" });

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("test", Arg.Any<CancellationToken>())
            .Returns(plugin);

        var function = new PluginActionFunction(
            "test", "fail", "Fails",
            null, pluginRouter);

        var result = await function.InvokeAsync(new AIFunctionArguments());

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("Error");
    }

    [Fact]
    public void Constructor_BuildsSchemaFromParameters()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        var function = new PluginActionFunction(
            "test", "action", "Test action",
            schema, Substitute.For<IPluginRouter>());

        function.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object);
        function.JsonSchema.GetProperty("title").GetString().Should().Be("test.action");
    }

    [Fact]
    public async Task InvokeCoreAsync_WithApprovalHandler_ChecksBeforeExecution()
    {
        var plugin = Substitute.For<IPlugin>();
        plugin.ExecuteActionAsync("greet", Arg.Any<PluginActionInput>(), Arg.Any<CancellationToken>())
            .Returns(new PluginActionResult { Success = true, Output = "Hello!" });

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("hello", Arg.Any<CancellationToken>())
            .Returns(plugin);

        var approvalHandler = Substitute.For<IToolApprovalHandler>();
        approvalHandler.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(ToolApprovalResult.Approve());

        var function = new PluginActionFunction(
            "hello", "greet", "Greets",
            null, pluginRouter, approvalHandler: approvalHandler);

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "World" }));

        result.Should().Be("Hello!");
        await approvalHandler.Received(1).RequestApprovalAsync(
            "hello.greet", Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeCoreAsync_WithRejectedApproval_ReturnsError()
    {
        var pluginRouter = Substitute.For<IPluginRouter>();

        var approvalHandler = Substitute.For<IToolApprovalHandler>();
        approvalHandler.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(ToolApprovalResult.Reject("Not allowed"));

        var function = new PluginActionFunction(
            "hello", "greet", "Greets",
            null, pluginRouter, approvalHandler: approvalHandler);

        var result = await function.InvokeAsync(new AIFunctionArguments());

        result.Should().NotBeNull();
        result!.ToString().Should().Contain("rejected");
        result!.ToString().Should().Contain("Not allowed");

        // Plugin should NOT have been called
        await pluginRouter.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
