using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins;

public sealed class DefaultPluginServices : Gantri.Abstractions.Plugins.IPluginServices
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    public DefaultPluginServices(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
    }

    public IPluginLogger GetLogger(string categoryName)
        => new PluginLoggerAdapter(_loggerFactory.CreateLogger(categoryName));

    public string? GetConfig(string key)
        => _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?[key];

    public IChatClient? GetChatClient(string? modelAlias = null)
        => _serviceProvider.GetService<IChatClient>();

    public T? GetService<T>(string? name = null) where T : class
    {
        if (name is null)
            return _serviceProvider.GetService<T>();

        if (_serviceProvider is IKeyedServiceProvider keyed)
            return keyed.GetKeyedService<T>(name);

        return _serviceProvider.GetService<T>();
    }
}

internal sealed class PluginLoggerAdapter : IPluginLogger
{
    private readonly ILogger _logger;

    public PluginLoggerAdapter(ILogger logger) => _logger = logger;

    public void Log(PluginLogLevel level, string message)
    {
        var msLevel = level switch
        {
            PluginLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            PluginLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            PluginLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            PluginLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
        _logger.Log(msLevel, "{Message}", message);
    }

    public void Debug(string message) => _logger.LogDebug("{Message}", message);
    public void Info(string message) => _logger.LogInformation("{Message}", message);
    public void Warning(string message) => _logger.LogWarning("{Message}", message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is not null)
            _logger.LogError(exception, "{Message}", message);
        else
            _logger.LogError("{Message}", message);
    }
}
