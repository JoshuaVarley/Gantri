using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gantri.Bridge.Tests;

public class GroupChatOrchestratorTests
{
    private static IHookPipeline CreatePassthroughPipeline()
    {
        var pipeline = Substitute.For<IHookPipeline>();
        pipeline.ExecuteAsync(
            Arg.Any<HookEvent>(),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = new HookContext(callInfo.ArgAt<HookEvent>(0));
                return new ValueTask<HookContext>(ctx);
            });
        return pipeline;
    }

    [Fact]
    public async Task RunAsync_ThrowsForUnknownParticipant()
    {
        var registry = new ModelProviderRegistry();
        var definitions = new Dictionary<string, AgentDefinition>();

        var factory = new GantriAgentFactory(
            registry, Substitute.For<IPluginRouter>(), Substitute.For<IMcpToolProvider>(),
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()));

        var orchestrator = new GroupChatOrchestrator(
            factory, CreatePassthroughPipeline(), new AgentDefinitionRegistry(definitions),
            NullLogger<GroupChatOrchestrator>.Instance);

        var act = () => orchestrator.RunAsync(
            ["nonexistent-agent"], "hello", cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
