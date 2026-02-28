using Gantri.Workflows;

namespace Gantri.Integration.Tests;

public class WorkflowContextTests
{
    [Fact]
    public void ResolveTemplate_InputVariable_ResolvesCorrectly()
    {
        var context = new WorkflowContext("test",
            new Dictionary<string, object?> { ["name"] = "World" });

        var result = context.ResolveTemplate("Hello, ${input.name}!");
        result.Should().Be("Hello, World!");
    }

    [Fact]
    public void ResolveTemplate_StepOutput_ResolvesCorrectly()
    {
        var context = new WorkflowContext("test");
        context.SetStepOutput("step1", "42");

        var result = context.ResolveTemplate("Result: ${steps.step1.output}");
        result.Should().Be("Result: 42");
    }

    [Fact]
    public void ResolveTemplate_UnknownVariable_PreservesOriginal()
    {
        var context = new WorkflowContext("test");

        var result = context.ResolveTemplate("Value: ${input.missing}");
        result.Should().Be("Value: ${input.missing}");
    }

    [Fact]
    public void ResolveTemplate_NullTemplate_ReturnsEmpty()
    {
        var context = new WorkflowContext("test");
        context.ResolveTemplate(null).Should().BeEmpty();
        context.ResolveTemplate("").Should().BeEmpty();
    }

    [Fact]
    public void StepOutputs_TrackedCorrectly()
    {
        var context = new WorkflowContext("test");
        context.SetStepOutput("a", "output-a");
        context.SetStepOutput("b", "output-b");

        context.StepOutputs.Should().HaveCount(2);
        context.GetStepOutput("a").Should().Be("output-a");
        context.GetStepOutput("b").Should().Be("output-b");
        context.GetStepOutput("c").Should().BeNull();
    }

    [Fact]
    public void ExecutionId_IsGenerated()
    {
        var context = new WorkflowContext("test");
        context.ExecutionId.Should().NotBeNullOrEmpty();
        context.ExecutionId.Should().HaveLength(12);
    }
}
