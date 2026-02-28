using Gantri.Abstractions.Hooks;
using Gantri.Bridge;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge.Tests;

public class HookMiddlewareStreamingTests
{
    [Fact]
    public async Task WithHooks_FiresBeforeAndAfterHooksForStreaming()
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

        // Mock GetStreamingResponseAsync to return a valid async enumerable
        var streamingResponse = AsyncEnumerableHelper.Empty<ChatResponseUpdate>();
        innerClient.GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(streamingResponse);

        var wrappedClient = innerClient.WithHooks("test-agent", hookPipeline);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };
        var result = wrappedClient.GetStreamingResponseAsync(messages);

        // Consume the stream to trigger hooks
        await foreach (var _ in result) { }

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

    private static class AsyncEnumerableHelper
    {
        public static IAsyncEnumerable<T> Empty<T>() => new EmptyAsyncEnumerable<T>();

        private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new EmptyAsyncEnumerator<T>();
        }

        private sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            public T Current => default!;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public ValueTask<bool> MoveNextAsync() => new(false);
        }
    }
}
