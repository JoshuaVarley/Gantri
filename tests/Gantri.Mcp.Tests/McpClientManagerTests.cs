using Gantri.Abstractions.Mcp;
using Gantri.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Mcp.Tests;

public class McpClientManagerTests
{
    private readonly McpClientManager _sut = new(NullLogger<McpClientManager>.Instance);

    private static IMcpServer CreateMockServer(string name, bool isConnected = false)
    {
        var server = Substitute.For<IMcpServer>();
        server.Name.Returns(name);
        server.Transport.Returns("stdio");
        server.IsConnected.Returns(isConnected);
        return server;
    }

    [Fact]
    public void RegisterServer_AddsServer()
    {
        var server = CreateMockServer("brave");

        _sut.RegisterServer(server);

        _sut.GetServerNames().Should().Contain("brave");
    }

    [Fact]
    public async Task GetToolsAsync_ForSpecificServer_ReturnsToolsFromServer()
    {
        var server = CreateMockServer("brave", isConnected: true);
        var expectedTools = new List<McpToolInfo>
        {
            new() { ServerName = "brave", ToolName = "search", Description = "Web search" },
            new() { ServerName = "brave", ToolName = "news", Description = "News search" }
        };
        server.DiscoverToolsAsync(Arg.Any<CancellationToken>()).Returns(expectedTools);

        _sut.RegisterServer(server);

        var tools = await _sut.GetToolsAsync("brave");

        tools.Should().HaveCount(2);
        tools.Should().Contain(t => t.ToolName == "search");
        tools.Should().Contain(t => t.ToolName == "news");
    }

    [Fact]
    public async Task GetToolsAsync_UnknownServer_ThrowsInvalidOperation()
    {
        var act = () => _sut.GetToolsAsync("nonexistent");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent*not found*");
    }

    [Fact]
    public async Task InvokeToolAsync_UnknownServer_ReturnsFail()
    {
        var result = await _sut.InvokeToolAsync("nonexistent", "tool");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent");
    }

    [Fact]
    public void GetServerNames_ReturnsRegisteredNames()
    {
        _sut.RegisterServer(CreateMockServer("brave"));
        _sut.RegisterServer(CreateMockServer("github"));

        var names = _sut.GetServerNames();

        names.Should().HaveCount(2);
        names.Should().Contain("brave");
        names.Should().Contain("github");
    }
}
