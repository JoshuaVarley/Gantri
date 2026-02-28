using Gantri.Bridge;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Bridge.Tests;

public class AfAgentSessionTests
{
    [Fact]
    public async Task CreateAsync_SetsProperties()
    {
        var mockClient = Substitute.For<IChatClient>();
        var agent = mockClient.AsAIAgent(instructions: "test", name: "test-agent");

        var session = await AfAgentSession.CreateAsync(agent, "test-agent", NullLogger<AfAgentSession>.Instance);

        session.AgentName.Should().Be("test-agent");
        session.SessionId.Should().NotBeNullOrEmpty();
        session.SessionId.Should().HaveLength(12);
    }

    [Fact]
    public async Task DisposeAsync_CompletesWithoutError()
    {
        var mockClient = Substitute.For<IChatClient>();
        var agent = mockClient.AsAIAgent(instructions: "test", name: "test-agent");

        var session = await AfAgentSession.CreateAsync(agent, "test-agent", NullLogger<AfAgentSession>.Instance);

        // DisposeAsync returns ValueTask — just await it directly
        await session.DisposeAsync();
    }
}
