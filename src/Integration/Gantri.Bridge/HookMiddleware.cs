using System.Runtime.CompilerServices;
using Gantri.Abstractions.Hooks;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge;

/// <summary>
/// IChatClient middleware that fires <c>agent:{name}:model-call:before/after</c> hooks
/// around inner client calls. Applied via the <c>.Use()</c> builder pattern.
/// </summary>
public static class HookMiddleware
{
    /// <summary>
    /// Wraps an <see cref="IChatClient"/> with hook pipeline integration for the given agent name.
    /// Fires before/after hooks around each model call.
    /// </summary>
    public static IChatClient WithHooks(this IChatClient innerClient, string agentName, IHookPipeline hookPipeline)
    {
        return innerClient.AsBuilder()
            .Use(
                getResponseFunc: async (messages, options, innerChatClient, cancellationToken) =>
                {
                    var beforeEvent = new HookEvent("agent", agentName, "model-call", HookTiming.Before);
                    await hookPipeline.ExecuteAsync(beforeEvent, _ => ValueTask.CompletedTask,
                        cancellationToken: cancellationToken);

                    var response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);

                    var afterEvent = new HookEvent("agent", agentName, "model-call", HookTiming.After);
                    var afterCtx = new HookContext(afterEvent, cancellationToken);
                    afterCtx.Set("response", response);
                    await hookPipeline.ExecuteAsync(afterEvent, _ => ValueTask.CompletedTask,
                        afterCtx, cancellationToken);

                    return response;
                },
                getStreamingResponseFunc: (messages, options, innerChatClient, cancellationToken) =>
                    WrapStreamingWithHooks(messages, options, innerChatClient, agentName, hookPipeline, cancellationToken))
            .Build();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> WrapStreamingWithHooks(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient innerChatClient,
        string agentName,
        IHookPipeline hookPipeline,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var beforeEvent = new HookEvent("agent", agentName, "model-call", HookTiming.Before);
        await hookPipeline.ExecuteAsync(beforeEvent, _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        await foreach (var update in innerChatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }

        var afterEvent = new HookEvent("agent", agentName, "model-call", HookTiming.After);
        await hookPipeline.ExecuteAsync(afterEvent, _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);
    }
}
