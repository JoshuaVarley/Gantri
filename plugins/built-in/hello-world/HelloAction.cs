using Gantri.Plugins.Sdk;

namespace HelloWorld.Plugin;

public sealed class HelloAction : ISdkPluginAction
{
    public string ActionName => "hello";
    public string Description => "Returns a greeting with echoed input";

    public Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var name = context.Parameters.TryGetValue("name", out var nameObj) && nameObj is string nameStr
            ? nameStr
            : "World";

        return Task.FromResult(ActionResult.Ok($"Hello, {name}! From the hello-world plugin."));
    }
}
