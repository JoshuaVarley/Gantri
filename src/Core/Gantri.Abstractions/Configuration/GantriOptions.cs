namespace Gantri.Abstractions.Configuration;

public sealed class GantriOptions
{
    public string Name { get; set; } = "Gantri";
    public string Version { get; set; } = "1.0.0";
    public string LogLevel { get; set; } = "Information";
    public string DataDir { get; set; } = "./data";
    public List<string> Imports { get; set; } = [];
}
