namespace Gantri.Plugins.Sdk;

public sealed class ActionResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? Error { get; init; }

    public static ActionResult Ok(object? output = null) => new() { Success = true, Output = output };
    public static ActionResult Fail(string error) => new() { Success = false, Error = error };
}
