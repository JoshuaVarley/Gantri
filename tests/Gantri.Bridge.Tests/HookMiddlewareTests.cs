using Gantri.Abstractions.Hooks;
using Gantri.Bridge;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge.Tests;

public class HookMiddlewareTests
{
    [Fact]
    public async Task WithHooks_FiresBeforeAndAfterHooks()
    {
        var hookPipeline = Substitute.For<IHookPipeline>();
        hookPipeline.ExecuteAsync(
            Arg.Any<HookEvent>(),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = new HookContext(callInfo.ArgAt<HookEvent>(0));
                return new ValueTask<HookContext>(ctx);
            });

        var innerClient = Substitute.For<IChatClient>();
        innerClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new List<ChatMessage>
            {
                new(ChatRole.Assistant, "response")
            }));

        var wrappedClient = innerClient.WithHooks("test-agent", hookPipeline);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        await wrappedClient.GetResponseAsync(messages);

        // Verify before hook was fired
        await hookPipeline.Received().ExecuteAsync(
            Arg.Is<HookEvent>(e => e.Action == "model-call" && e.Timing == HookTiming.Before),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>());

        // Verify after hook was fired
        await hookPipeline.Received().ExecuteAsync(
            Arg.Is<HookEvent>(e => e.Action == "model-call" && e.Timing == HookTiming.After),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>());
    }
}
