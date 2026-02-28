using Gantri.Abstractions.Configuration;
using Gantri.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Workflows.Tests;

public class ApprovalStepHandlerTests
{
    private readonly ApprovalStepHandler _handler = new(NullLogger<ApprovalStepHandler>.Instance);

    [Fact]
    public void StepType_IsApproval()
    {
        _handler.StepType.Should().Be("approval");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsApprovalPending()
    {
        var step = new WorkflowStepDefinition
        {
            Id = "approve-deploy",
            Type = "approval",
            Input = "Please approve deployment to production"
        };
        var context = new WorkflowContext("test-workflow");

        var result = await _handler.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().BeOfType<ApprovalPending>();

        var pending = (ApprovalPending)result.Output!;
        pending.StepId.Should().Be("approve-deploy");
        pending.Message.Should().Be("Please approve deployment to production");
        pending.ExecutionId.Should().Be(context.ExecutionId);
    }

    [Fact]
    public async Task ExecuteAsync_NullInput_UsesDefaultMessage()
    {
        var step = new WorkflowStepDefinition
        {
            Id = "my-gate",
            Type = "approval",
            Input = null
        };
        var context = new WorkflowContext("test-workflow");

        var result = await _handler.ExecuteAsync(step, context);
        var pending = (ApprovalPending)result.Output!;

        pending.Message.Should().Contain("my-gate");
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesTemplateVariables()
    {
        var step = new WorkflowStepDefinition
        {
            Id = "approve",
            Type = "approval",
            Input = "Approve release of ${input.version}"
        };
        var context = new WorkflowContext("test-workflow",
            new Dictionary<string, object?> { ["version"] = "2.0.0" });

        var result = await _handler.ExecuteAsync(step, context);
        var pending = (ApprovalPending)result.Output!;

        pending.Message.Should().Be("Approve release of 2.0.0");
    }
}
