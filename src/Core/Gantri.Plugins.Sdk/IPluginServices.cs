using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Sdk;

public interface IPluginServices : Gantri.Abstractions.Plugins.IPluginServices;

public interface ILogger : IPluginLogger;

public enum LogLevel
{
    Debug = PluginLogLevel.Debug,
    Information = PluginLogLevel.Information,
    Warning = PluginLogLevel.Warning,
    Error = PluginLogLevel.Error
}
