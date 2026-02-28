namespace Gantri.Abstractions.Configuration;

public sealed class SchedulingOptions
{
    public Dictionary<string, ScheduledJobDefinition> Jobs { get; set; } = new();
    public SchedulingStorageOptions Storage { get; set; } = new();
}

public sealed class SchedulingStorageOptions
{
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=data/gantri-scheduling.db";
}

public sealed class ScheduledJobDefinition
{
    public string Type { get; set; } = "workflow"; // workflow, agent, plugin
    public string? Workflow { get; set; }
    public string? Agent { get; set; }
    public string? Plugin { get; set; }
    public string? Action { get; set; }
    public string? Input { get; set; }
    public string Cron { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object?> Parameters { get; set; } = new();
}
