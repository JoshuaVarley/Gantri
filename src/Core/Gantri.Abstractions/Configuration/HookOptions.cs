namespace Gantri.Abstractions.Configuration;

public sealed class HookOptions
{
    public List<HookBinding> Bindings { get; set; } = [];
}

public sealed class HookBinding
{
    public string Event { get; set; } = string.Empty;
    public string Plugin { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public int Priority { get; set; } = 500;
}
