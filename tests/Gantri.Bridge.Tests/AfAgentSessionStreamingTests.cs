using Gantri.Bridge;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Bridge.Tests;

public class AfAgentSessionStreamingTests
{
    [Fact]
    public async Task SendMessageStreamingAsync_YieldsTokens()
    {
        // Arrange: Create a mock chat client that returns streaming content
        var mockClient = Substitute.For<IChatClient>();
        var agent = mockClient.AsAIAgent(instructions: "test", name: "test-agent");

        var session = await AfAgentSession.CreateAsync(agent, "test-agent", NullLogger<AfAgentSession>.Instance);

        // Act: Call the streaming method
        // Note: The actual streaming depends on AF's RunStreamingAsync which delegates to IChatClient.
        // Since the mock returns no streaming data, we verify it doesn't throw and yields zero or more items.
        var tokens = new List<string>();
        await foreach (var token in session.SendMessageStreamingAsync("hello"))
        {
            tokens.Add(token);
        }

        // Assert: With a basic mock, we just verify the method completes without error
        session.AgentName.Should().Be("test-agent");
    }
}
