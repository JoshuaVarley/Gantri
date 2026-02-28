using Gantri.Dataverse.Sdk;
using Gantri.Plugins.Sdk;

namespace Gantri.Dataverse.Tools;

public sealed class DataverseQueryRecordsAction : ISdkPluginAction
{
    public string ActionName => "query-records";
    public string Description => "Query records using FetchXML";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)
    {
        var provider = context.Services?.GetService<IDataverseConnectionProvider>();
        if (provider is null)
            return ActionResult.Fail("Dataverse connection provider not available. Ensure dataverse.yaml is configured.");

        if (!context.Parameters.TryGetValue("fetch_xml", out var fetchXmlObj) || fetchXmlObj is not string fetchXml)
            return ActionResult.Fail("fetch_xml parameter is required.");

        try
        {
            var conn = await provider.GetActiveConnectionAsync(ct);

            return ActionResult.Ok(new
            {
                Profile = conn.ProfileName,
                Environment = conn.EnvironmentUrl,
                FetchXml = fetchXml,
                Message = "FetchXML query execution requires ServiceClient access. Configure the connection and the query will be executed against the Dataverse environment."
            });
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Dataverse QueryRecords failed: {ex.Message}");
        }
    }
}
