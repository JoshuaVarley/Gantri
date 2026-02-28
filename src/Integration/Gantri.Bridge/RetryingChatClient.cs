using Azure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Gantri.Bridge;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that wraps the inner client with Polly-based
/// retry (exponential backoff + jitter) and per-call timeout resilience.
/// Handles Azure OpenAI transient errors (HTTP 429, 503) and network failures.
/// </summary>
public sealed class RetryingChatClient : DelegatingChatClient
{
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<RetryingChatClient> _logger;

    public RetryingChatClient(IChatClient innerClient, ILogger<RetryingChatClient> logger)
        : base(innerClient)
    {
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<RequestFailedException>(ex => ex.Status is 429 or 503),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Retry attempt {Attempt} after {Delay}ms — {ExceptionMessage}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return default;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(60),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Model call timed out after {Timeout}s", args.Timeout.TotalSeconds);
                    return default;
                }
            })
            .Build();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async ct => await base.GetResponseAsync(messages, options, ct),
            cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        // Streaming bypasses the resilience pipeline:
        // - Retry: a partially-consumed stream cannot be replayed
        // - Timeout: streaming responses with tool calls can run for minutes
        // Connection-level timeouts are handled by the HTTP client's NetworkTimeout.
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}
