namespace Gantri.Abstractions.Configuration;

public sealed class TelemetryOptions
{
    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "gantri";
    public string ServiceVersion { get; set; } = "1.0.0";
    public TraceOptions Traces { get; set; } = new();
    public MetricExportOptions Metrics { get; set; } = new();
    public LogExportOptions Logs { get; set; } = new();
    public Dictionary<string, string> ResourceAttributes { get; set; } = new();
}

public sealed class TraceOptions
{
    public string Exporter { get; set; } = "otlp";
    public string? Endpoint { get; set; }
    public SamplingOptions Sampling { get; set; } = new();
    public bool EnableSensitiveData { get; set; } = false;
}

public sealed class SamplingOptions
{
    public string Strategy { get; set; } = "always_on";
    public double Ratio { get; set; } = 1.0;
}

public sealed class MetricExportOptions
{
    public string Exporter { get; set; } = "otlp";
    public string? Endpoint { get; set; }
    public int ExportIntervalMs { get; set; } = 30000;
}

public sealed class LogExportOptions
{
    public string Exporter { get; set; } = "otlp";
    public string? Endpoint { get; set; }
    public string MinLevel { get; set; } = "Information";
}
