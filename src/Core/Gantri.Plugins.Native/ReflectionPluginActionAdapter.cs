using System.Reflection;
using Gantri.Abstractions.Plugins;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native;

/// <summary>
/// Bridges plugin actions loaded in isolated AssemblyLoadContexts where direct
/// type casting to ISdkPluginAction fails due to type identity differences.
/// Uses reflection to invoke the action methods.
/// </summary>
internal sealed class ReflectionPluginActionAdapter
{
    private readonly object _instance;
    private readonly MethodInfo _executeMethod;
    private readonly Type _actionContextType;

    public string ActionName { get; }
    public string Description { get; }

    public ReflectionPluginActionAdapter(object instance, Type type)
    {
        _instance = instance;

        ActionName = (string)(type.GetProperty("ActionName")?.GetValue(instance)
            ?? throw new InvalidOperationException($"Type {type.FullName} missing ActionName property"));

        Description = (string)(type.GetProperty("Description")?.GetValue(instance) ?? "");

        // Find ExecuteAsync by name since types differ across AssemblyLoadContexts
        _executeMethod = type.GetMethods()
            .FirstOrDefault(m => m.Name == "ExecuteAsync" && m.GetParameters().Length == 2)
            ?? throw new InvalidOperationException($"Type {type.FullName} missing ExecuteAsync method");

        // Get the ActionContext type from the plugin's assembly context
        _actionContextType = _executeMethod.GetParameters()[0].ParameterType;
    }

    public async Task<PluginActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        // If the types are from the same context (direct cast works), use it
        if (_instance is ISdkPluginAction directAction)
        {
            var ctx = new ActionContext
            {
                ActionName = input.ActionName,
                Parameters = input.Parameters,
                CancellationToken = cancellationToken,
                WorkingDirectory = input.WorkingDirectory
            };
            var result = await directAction.ExecuteAsync(ctx, cancellationToken);
            return result.Success
                ? PluginActionResult.Ok(result.Output)
                : PluginActionResult.Fail(result.Error ?? "Unknown error");
        }

        // Cross-context: build ActionContext via reflection using the plugin's type
        var context = CreateActionContextReflection(input, cancellationToken);
        var task = _executeMethod.Invoke(_instance, [context, cancellationToken]);

        if (task is null)
            return PluginActionResult.Fail("ExecuteAsync returned null");

        await (Task)task;

        // Get the result from the Task<ActionResult>
        var resultProp = task.GetType().GetProperty("Result");
        var actionResult = resultProp?.GetValue(task);

        if (actionResult is null)
            return PluginActionResult.Fail("ExecuteAsync returned null result");

        var successProp = actionResult.GetType().GetProperty("Success");
        var outputProp = actionResult.GetType().GetProperty("Output");
        var errorProp = actionResult.GetType().GetProperty("Error");

        var success = (bool)(successProp?.GetValue(actionResult) ?? false);
        var output = outputProp?.GetValue(actionResult);
        var error = errorProp?.GetValue(actionResult) as string;

        return success ? PluginActionResult.Ok(output) : PluginActionResult.Fail(error ?? "Unknown error");
    }

    private object CreateActionContextReflection(PluginActionInput input, CancellationToken cancellationToken)
    {
        var context = Activator.CreateInstance(_actionContextType)!;
        _actionContextType.GetProperty("ActionName")?.SetValue(context, input.ActionName);
        _actionContextType.GetProperty("Parameters")?.SetValue(context, input.Parameters);
        _actionContextType.GetProperty("CancellationToken")?.SetValue(context, cancellationToken);
        _actionContextType.GetProperty("WorkingDirectory")?.SetValue(context, input.WorkingDirectory);
        return context;
    }
}
