using Gantri.Abstractions.Hooks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Hooks.Tests;

public class HookPipelineTests
{
    private static (HookPipeline pipeline, HookRegistry registry) Create()
    {
        var registry = new HookRegistry();
        var executor = new HookExecutor(NullLogger<HookExecutor>.Instance);
        var pipeline = new HookPipeline(registry, executor, NullLogger<HookPipeline>.Instance);
        return (pipeline, registry);
    }

    [Fact]
    public async Task Execute_NoHooks_RunsOperation()
    {
        var (pipeline, _) = Create();
        var operationRan = false;

        await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            operationRan = true;
            return ValueTask.CompletedTask;
        });

        operationRan.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_BeforeHooks_RunBeforeOperation()
    {
        var (pipeline, registry) = Create();
        var order = new List<string>();

        registry.Register(new TestHook("before1", "agent:*:*:before", HookTiming.Before, 100,
            (ctx, _) => { order.Add("before1"); return ValueTask.CompletedTask; }));

        await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            order.Add("operation");
            return ValueTask.CompletedTask;
        });

        order.Should().BeEquivalentTo(["before1", "operation"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Execute_AfterHooks_RunAfterOperation()
    {
        var (pipeline, registry) = Create();
        var order = new List<string>();

        registry.Register(new TestHook("after1", "agent:*:*:before", HookTiming.After, 100,
            (ctx, _) => { order.Add("after1"); return ValueTask.CompletedTask; }));

        await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            order.Add("operation");
            return ValueTask.CompletedTask;
        });

        order.Should().BeEquivalentTo(["operation", "after1"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Execute_BeforeAndAfterHooks_CorrectOrder()
    {
        var (pipeline, registry) = Create();
        var order = new List<string>();

        registry.Register(new TestHook("before", "agent:*:*:before", HookTiming.Before, 100,
            (ctx, _) => { order.Add("before"); return ValueTask.CompletedTask; }));
        registry.Register(new TestHook("after", "agent:*:*:before", HookTiming.After, 100,
            (ctx, _) => { order.Add("after"); return ValueTask.CompletedTask; }));

        await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            order.Add("operation");
            return ValueTask.CompletedTask;
        });

        order.Should().BeEquivalentTo(["before", "operation", "after"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Execute_CancellationInBeforeHook_SkipsOperation()
    {
        var (pipeline, registry) = Create();
        var operationRan = false;

        registry.Register(new TestHook("canceller", "agent:*:*:before", HookTiming.Before, 100,
            (ctx, _) => { ctx.Cancel("blocked"); return ValueTask.CompletedTask; }));

        var result = await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            operationRan = true;
            return ValueTask.CompletedTask;
        });

        operationRan.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
        result.CancellationReason.Should().Be("blocked");
    }

    [Fact]
    public async Task Execute_PriorityOrder_LowerNumberRunsFirst()
    {
        var (pipeline, registry) = Create();
        var order = new List<string>();

        registry.Register(new TestHook("low", "agent:*:*:before", HookTiming.Before, 900,
            (ctx, _) => { order.Add("low"); return ValueTask.CompletedTask; }));
        registry.Register(new TestHook("high", "agent:*:*:before", HookTiming.Before, 50,
            (ctx, _) => { order.Add("high"); return ValueTask.CompletedTask; }));

        await pipeline.ExecuteAsync("agent:test:action:before", _ => ValueTask.CompletedTask);

        order.Should().BeEquivalentTo(["high", "low"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Execute_ErrorHooks_RunOnFailure()
    {
        var (pipeline, registry) = Create();
        var errorHookRan = false;

        registry.Register(new TestHook("error-handler", "agent:*:*:before", HookTiming.OnError, 100,
            (ctx, _) =>
            {
                errorHookRan = true;
                ctx.Error = null; // Suppress the error
                return ValueTask.CompletedTask;
            }));

        await pipeline.ExecuteAsync("agent:test:action:before", _ =>
            throw new InvalidOperationException("test error"));

        errorHookRan.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_AroundHooks_WrapOperation()
    {
        var (pipeline, registry) = Create();
        var order = new List<string>();

        registry.Register(new TestHook("around", "agent:*:*:before", HookTiming.Around, 100,
            async (ctx, next) =>
            {
                order.Add("around-before");
                if (next is not null) await next();
                order.Add("around-after");
            }));

        await pipeline.ExecuteAsync("agent:test:action:before", ctx =>
        {
            order.Add("operation");
            return ValueTask.CompletedTask;
        });

        order.Should().BeEquivalentTo(["around-before", "operation", "around-after"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Execute_PropertyPropagation_WorksAcrossHooks()
    {
        var (pipeline, registry) = Create();

        registry.Register(new TestHook("setter", "agent:*:*:before", HookTiming.Before, 100,
            (ctx, _) => { ctx.Set("key", "from-before"); return ValueTask.CompletedTask; }));

        registry.Register(new TestHook("reader", "agent:*:*:before", HookTiming.After, 100,
            (ctx, _) =>
            {
                ctx.Get<string>("key").Should().Be("from-before");
                return ValueTask.CompletedTask;
            }));

        await pipeline.ExecuteAsync("agent:test:action:before", _ => ValueTask.CompletedTask);
    }
}
