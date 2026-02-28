using Gantri.Dataverse.Sdk;
using Gantri.Plugins.Sdk;

namespace Gantri.Dataverse.Tools;

public sealed class DataverseListEntitiesAction : ISdkPluginAction
{
    public string ActionName => "list-entities";
    public string Description => "List all entities/tables in the Dataverse environment";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)
    {
        var provider = context.Services?.GetService<IDataverseConnectionProvider>();
        if (provider is null)
            return ActionResult.Fail("Dataverse connection provider not available. Ensure dataverse.yaml is configured.");

        var filter = context.Parameters.TryGetValue("filter", out var f) && f is string fs ? fs : null;
        var customOnly = context.Parameters.TryGetValue("custom_only", out var c) && c is bool cb && cb;

        try
        {
            var conn = await provider.GetActiveConnectionAsync(ct);

            return ActionResult.Ok(new
            {
                Profile = conn.ProfileName,
                Environment = conn.EnvironmentUrl,
                Filter = filter,
                CustomOnly = customOnly,
                Message = "Entity listing requires ServiceClient access. Configure the connection and use the Dataverse SDK metadata API."
            });
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Dataverse ListEntities failed: {ex.Message}");
        }
    }
}
