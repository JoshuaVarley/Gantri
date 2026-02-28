using Gantri.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Workflows.Tests;

public class WorkflowStateManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkflowStateManager _manager;

    public WorkflowStateManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-test-{Guid.NewGuid():N}");
        _manager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var state = new WorkflowState
        {
            ExecutionId = "exec-001",
            WorkflowName = "test-wf",
            Status = "running",
            CompletedStepIndex = 2,
            CurrentStep = "step-3",
            StepOutputs = new Dictionary<string, object?> { ["step-1"] = "output-1" }
        };

        await _manager.SaveStateAsync(state);
        var loaded = await _manager.LoadStateAsync("exec-001");

        loaded.Should().NotBeNull();
        loaded!.ExecutionId.Should().Be("exec-001");
        loaded.WorkflowName.Should().Be("test-wf");
        loaded.Status.Should().Be("running");
        loaded.CompletedStepIndex.Should().Be(2);
        loaded.CurrentStep.Should().Be("step-3");
    }

    [Fact]
    public async Task LoadState_NonexistentId_ReturnsNull()
    {
        var loaded = await _manager.LoadStateAsync("nonexistent");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task RemoveState_DeletesFile()
    {
        var state = new WorkflowState
        {
            ExecutionId = "exec-002",
            WorkflowName = "test-wf",
            Status = "running"
        };

        await _manager.SaveStateAsync(state);
        var loaded = await _manager.LoadStateAsync("exec-002");
        loaded.Should().NotBeNull();

        await _manager.RemoveStateAsync("exec-002");
        var afterRemove = await _manager.LoadStateAsync("exec-002");
        afterRemove.Should().BeNull();
    }

    [Fact]
    public async Task RemoveState_NonexistentId_DoesNotThrow()
    {
        var act = () => _manager.RemoveStateAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListActiveStates_ReturnsOnlyActive()
    {
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e1", WorkflowName = "wf1", Status = "running"
        });
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e2", WorkflowName = "wf2", Status = "waiting_approval"
        });
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e3", WorkflowName = "wf3", Status = "failed"
        });
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e4", WorkflowName = "wf4", Status = "completed"
        });

        var active = await _manager.ListActiveStatesAsync();

        active.Should().HaveCount(2);
        active.Should().Contain(s => s.ExecutionId == "e1");
        active.Should().Contain(s => s.ExecutionId == "e2");
    }

    [Fact]
    public async Task SaveState_OverwritesExisting()
    {
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e1", WorkflowName = "wf1", Status = "running", CompletedStepIndex = 0
        });

        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e1", WorkflowName = "wf1", Status = "waiting_approval", CompletedStepIndex = 3
        });

        var loaded = await _manager.LoadStateAsync("e1");
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be("waiting_approval");
        loaded.CompletedStepIndex.Should().Be(3);
    }

    [Fact]
    public async Task SaveState_WithError_PersistsError()
    {
        await _manager.SaveStateAsync(new WorkflowState
        {
            ExecutionId = "e1",
            WorkflowName = "wf1",
            Status = "failed",
            Error = "Something went wrong"
        });

        var loaded = await _manager.LoadStateAsync("e1");
        loaded.Should().NotBeNull();
        loaded!.Error.Should().Be("Something went wrong");
    }
}
