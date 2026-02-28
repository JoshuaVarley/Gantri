using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Gantri.Workflows;

/// <summary>
/// Execution context for a workflow run. Stores step outputs and resolves variable references.
/// Variables follow the format: ${input.key}, ${steps.stepId.output}, ${env.VAR_NAME}
/// </summary>
public sealed partial class WorkflowContext
{
    private readonly Dictionary<string, object?> _input;
    private readonly ConcurrentDictionary<string, object?> _stepOutputs = new(StringComparer.OrdinalIgnoreCase);

    public string WorkflowName { get; }
    public string ExecutionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public IReadOnlyDictionary<string, object?> StepOutputs => _stepOutputs;

    public WorkflowContext(string workflowName, IReadOnlyDictionary<string, object?>? input = null)
    {
        WorkflowName = workflowName;
        _input = input is not null ? new Dictionary<string, object?>(input) : new();
    }

    public void SetStepOutput(string stepId, object? output)
    {
        _stepOutputs[stepId] = output;
    }

    public object? GetStepOutput(string stepId)
    {
        _stepOutputs.TryGetValue(stepId, out var output);
        return output;
    }

    /// <summary>
    /// Resolves variable references in a string template.
    /// </summary>
    public string ResolveTemplate(string? template)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return VariablePattern().Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            var parts = path.Split('.', 2);

            return parts[0] switch
            {
                "input" when parts.Length > 1 =>
                    _input.TryGetValue(parts[1], out var val) ? val?.ToString() ?? "" : match.Value,
                "steps" when parts.Length > 1 => ResolveStepRef(parts[1]),
                "env" when parts.Length > 1 =>
                    Environment.GetEnvironmentVariable(parts[1]) ?? match.Value,
                _ => match.Value
            };
        });
    }

    private string ResolveStepRef(string path)
    {
        // path = "stepId.output" or "stepId"
        var parts = path.Split('.', 2);
        var stepId = parts[0];
        return _stepOutputs.TryGetValue(stepId, out var output) ? output?.ToString() ?? "" : "";
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();
}
