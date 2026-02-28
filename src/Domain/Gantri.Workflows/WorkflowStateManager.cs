using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gantri.Workflows;

/// <summary>
/// Checkpoints workflow state to disk for persistence and resumption.
/// State files are stored at {data_dir}/workflows/{execution_id}.json.
/// </summary>
public sealed class WorkflowStateManager
{
    private readonly string _stateDir;
    private readonly ILogger<WorkflowStateManager> _logger;

    public WorkflowStateManager(string dataDir, ILogger<WorkflowStateManager> logger)
    {
        _stateDir = Path.Combine(dataDir, "workflows");
        Directory.CreateDirectory(_stateDir);
        _logger = logger;
    }

    public async Task SaveStateAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        var path = GetStatePath(state.ExecutionId);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
        _logger.LogDebug("Saved workflow state for execution {ExecutionId}", state.ExecutionId);
    }

    public async Task<WorkflowState?> LoadStateAsync(string executionId, CancellationToken cancellationToken = default)
    {
        var path = GetStatePath(executionId);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<WorkflowState>(json);
    }

    public async Task<IReadOnlyList<WorkflowState>> ListActiveStatesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_stateDir)) return [];

        var states = new List<WorkflowState>();
        foreach (var file in Directory.GetFiles(_stateDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var state = JsonSerializer.Deserialize<WorkflowState>(json);
                if (state is not null && state.Status is "running" or "paused" or "waiting_approval")
                {
                    states.Add(state);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read workflow state from {Path}", file);
            }
        }
        return states;
    }

    public Task RemoveStateAsync(string executionId, CancellationToken cancellationToken = default)
    {
        var path = GetStatePath(executionId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogDebug("Removed workflow state for execution {ExecutionId}", executionId);
        }
        return Task.CompletedTask;
    }

    private string GetStatePath(string executionId) => Path.Combine(_stateDir, $"{executionId}.json");
}

public sealed class WorkflowState
{
    public string ExecutionId { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public string Status { get; set; } = "running"; // running, paused, waiting_approval, completed, failed
    public DateTimeOffset StartTime { get; set; }
    public int CompletedStepIndex { get; set; }
    public Dictionary<string, object?> Input { get; set; } = new();
    public Dictionary<string, object?> StepOutputs { get; set; } = new();
    public string? CurrentStep { get; set; }
    public string? Error { get; set; }
}
