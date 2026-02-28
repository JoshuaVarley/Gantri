using Microsoft.Extensions.AI;

namespace Gantri.Abstractions.Plugins;

public interface IPluginServices
{
    IPluginLogger GetLogger(string categoryName);
    string? GetConfig(string key);
    IChatClient? GetChatClient(string? modelAlias = null);
    T? GetService<T>(string? name = null) where T : class;
}

public interface IPluginLogger
{
    void Log(PluginLogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public enum PluginLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}
