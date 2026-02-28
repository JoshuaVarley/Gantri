using Gantri.Dataverse.Sdk;
using Gantri.Plugins.Sdk;

namespace Gantri.Dataverse.Tools;

public sealed class DataverseWhoAmIAction : ISdkPluginAction
{
    public string ActionName => "who-am-i";
    public string Description => "Get current user and organization info from the active Dataverse environment";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)
    {
        var provider = context.Services?.GetService<IDataverseConnectionProvider>();
        if (provider is null)
            return ActionResult.Fail("Dataverse connection provider not available. Ensure dataverse.yaml is configured.");

        var profileName = context.Parameters.TryGetValue("profile", out var p) && p is string s ? s : null;

        try
        {
            var conn = profileName is not null
                ? await provider.GetConnectionAsync(profileName, ct)
                : await provider.GetActiveConnectionAsync(ct);

            return ActionResult.Ok(new
            {
                Profile = conn.ProfileName,
                Environment = conn.EnvironmentUrl,
                Organization = conn.OrganizationName,
                DisplayName = conn.DisplayName
            });
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Dataverse WhoAmI failed: {ex.Message}");
        }
    }
}
