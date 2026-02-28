using Gantri.Abstractions.Hooks;
using Microsoft.Extensions.Logging;

namespace Gantri.Hooks;

public sealed class HookExecutor
{
    private readonly ILogger<HookExecutor> _logger;

    public HookExecutor(ILogger<HookExecutor> logger)
    {
        _logger = logger;
    }

    public async ValueTask ExecuteBeforeHooksAsync(IReadOnlyList<IHook> hooks, HookContext context)
    {
        foreach (var hook in hooks)
        {
            if (context.IsCancelled)
                break;

            try
            {
                await hook.ExecuteAsync(context);
                _logger.LogDebug("Before hook '{HookName}' executed for event '{Event}'", hook.Name, context.Event.Pattern);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Before hook '{HookName}' failed for event '{Event}'", hook.Name, context.Event.Pattern);
                throw;
            }
        }
    }

    public async ValueTask ExecuteAfterHooksAsync(IReadOnlyList<IHook> hooks, HookContext context)
    {
        foreach (var hook in hooks)
        {
            try
            {
                await hook.ExecuteAsync(context);
                _logger.LogDebug("After hook '{HookName}' executed for event '{Event}'", hook.Name, context.Event.Pattern);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "After hook '{HookName}' failed for event '{Event}'", hook.Name, context.Event.Pattern);
                throw;
            }
        }
    }

    public async ValueTask ExecuteErrorHooksAsync(IReadOnlyList<IHook> hooks, HookContext context)
    {
        foreach (var hook in hooks)
        {
            try
            {
                await hook.ExecuteAsync(context);
                _logger.LogDebug("Error hook '{HookName}' executed for event '{Event}'", hook.Name, context.Event.Pattern);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error hook '{HookName}' itself failed for event '{Event}'", hook.Name, context.Event.Pattern);
            }
        }
    }

    public async ValueTask ExecuteAroundHookAsync(IHook hook, HookContext context, Func<ValueTask> next)
    {
        try
        {
            await hook.ExecuteAsync(context, next);
            _logger.LogDebug("Around hook '{HookName}' executed for event '{Event}'", hook.Name, context.Event.Pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Around hook '{HookName}' failed for event '{Event}'", hook.Name, context.Event.Pattern);
            throw;
        }
    }
}
