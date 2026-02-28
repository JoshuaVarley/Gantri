using Microsoft.Extensions.AI;

namespace Gantri.Plugins.Sdk;

public interface IPluginServices
{
    ILogger GetLogger(string categoryName);
    string? GetConfig(string key);
    IChatClient? GetChatClient(string? modelAlias = null);
}

public interface ILogger
{
    void Log(LogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}
