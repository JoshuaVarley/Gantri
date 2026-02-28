namespace Gantri.Abstractions.Plugins;

[Flags]
public enum PluginCapability
{
    None = 0,
    Log = 1 << 0,
    ConfigRead = 1 << 1,
    AiComplete = 1 << 2,
    FsRead = 1 << 3,
    FsWrite = 1 << 4,
    HttpRequest = 1 << 5,
    McpCall = 1 << 6,
    ProcessExec = 1 << 7
}
