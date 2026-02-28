using System.Diagnostics;
using Gantri.Abstractions.Hooks;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Hooks;

public sealed class HookPipeline : IHookPipeline
{
    private readonly HookRegistry _registry;
    private readonly HookExecutor _executor;
    private readonly ILogger<HookPipeline> _logger;

    public HookPipeline(HookRegistry registry, HookExecutor executor, ILogger<HookPipeline> logger)
    {
        _registry = registry;
        _executor = executor;
        _logger = logger;
    }

    public ValueTask<HookContext> ExecuteAsync(
        string eventPattern,
        Func<HookContext, ValueTask> operation,
        HookContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var hookEvent = HookEvent.Parse(eventPattern);
        return ExecuteAsync(hookEvent, operation, context, cancellationToken);
    }

    public async ValueTask<HookContext> ExecuteAsync(
        HookEvent hookEvent,
        Func<HookContext, ValueTask> operation,
        HookContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= new HookContext(hookEvent, cancellationToken);
        var pattern = hookEvent.Pattern;

        using var activity = GantriActivitySources.Hooks.StartActivity("gantri.hooks.pipeline");
        activity?.SetTag(GantriSemanticConventions.HookEvent, pattern);

        var beforeHooks = _registry.GetMatchingHooks(pattern, HookTiming.Before);
        var afterHooks = _registry.GetMatchingHooks(pattern, HookTiming.After);
        var errorHooks = _registry.GetMatchingHooks(pattern, HookTiming.OnError);
        var aroundHooks = _registry.GetMatchingHooks(pattern, HookTiming.Around);

        // Fast path: no hooks registered
        if (beforeHooks.Count == 0 && afterHooks.Count == 0 && errorHooks.Count == 0 && aroundHooks.Count == 0)
        {
            await operation(context);
            return context;
        }

        GantriMeters.HookExecutionsTotal.Add(1, new KeyValuePair<string, object?>(GantriSemanticConventions.HookEvent, pattern));
        var sw = Stopwatch.StartNew();

        try
        {
            // Execute before hooks
            await _executor.ExecuteBeforeHooksAsync(beforeHooks, context);

            if (context.IsCancelled)
            {
                _logger.LogInformation("Operation cancelled by hook for event '{Event}': {Reason}", pattern, context.CancellationReason);
                GantriMeters.HookCancellations.Add(1, new KeyValuePair<string, object?>(GantriSemanticConventions.HookEvent, pattern));
                activity?.SetTag(GantriSemanticConventions.HookCancelled, true);
                return context;
            }

            // Build around hook chain wrapping the core operation
            Func<ValueTask> coreOperation = () => operation(context);
            var wrappedOperation = coreOperation;

            // Around hooks wrap in reverse order so the first around hook is outermost
            for (var i = aroundHooks.Count - 1; i >= 0; i--)
            {
                var hook = aroundHooks[i];
                var next = wrappedOperation;
                wrappedOperation = () => _executor.ExecuteAroundHookAsync(hook, context, next);
            }

            await wrappedOperation();

            // Execute after hooks
            await _executor.ExecuteAfterHooksAsync(afterHooks, context);
        }
        catch (Exception ex)
        {
            context.Error = ex;
            _logger.LogError(ex, "Operation failed for event '{Event}'", pattern);

            if (errorHooks.Count > 0)
            {
                await _executor.ExecuteErrorHooksAsync(errorHooks, context);
            }

            if (context.Error is not null)
                throw;
        }
        finally
        {
            sw.Stop();
            GantriMeters.HookExecutionsDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(GantriSemanticConventions.HookEvent, pattern));
        }

        return context;
    }
}
