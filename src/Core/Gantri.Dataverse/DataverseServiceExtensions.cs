using Gantri.Abstractions.Configuration;
using Gantri.Dataverse.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gantri.Dataverse;

public static class DataverseServiceExtensions
{
    public static IServiceCollection AddGantriDataverse(this IServiceCollection services, DataverseOptions? options = null)
    {
        var dataverseOptions = options ?? new DataverseOptions();

        services.AddSingleton<DataverseTokenProvider>();
        services.AddSingleton<IDataverseConnectionProvider>(sp =>
        {
            var tokenProvider = sp.GetRequiredService<DataverseTokenProvider>();
            var logger = sp.GetRequiredService<ILogger<DataverseConnectionProvider>>();
            return new DataverseConnectionProvider(dataverseOptions, tokenProvider, logger);
        });
        return services;
    }
}
