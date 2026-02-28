namespace Gantri.Abstractions.Configuration;

public sealed class WorkerOptions
{
    public WorkerMcpOptions Mcp { get; set; } = new();
}

public sealed class WorkerMcpOptions
{
    public string Transport { get; set; } = "stdio";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5100;
    public WorkerAuthOptions Auth { get; set; } = new();
}

public sealed class WorkerAuthOptions
{
    public string Type { get; set; } = "none";
    public string? Key { get; set; }
}
