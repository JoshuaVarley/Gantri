using Gantri.Abstractions.Hooks;

namespace Gantri.Hooks.Tests;

public class HookRegistryTests
{
    [Fact]
    public void Register_AddsHook()
    {
        var registry = new HookRegistry();
        var hook = new TestHook("test", "agent:*:*:before", HookTiming.Before);

        registry.Register(hook);

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Deregister_RemovesHook()
    {
        var registry = new HookRegistry();
        registry.Register(new TestHook("test", "agent:*:*:before", HookTiming.Before));

        registry.Deregister("test").Should().BeTrue();
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void Deregister_NonExistent_ReturnsFalse()
    {
        var registry = new HookRegistry();
        registry.Deregister("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void GetMatchingHooks_ReturnsMatchesInPriorityOrder()
    {
        var registry = new HookRegistry();
        registry.Register(new TestHook("low", "agent:*:*:before", HookTiming.Before, 900));
        registry.Register(new TestHook("high", "agent:*:*:before", HookTiming.Before, 100));
        registry.Register(new TestHook("mid", "agent:*:*:before", HookTiming.Before, 500));

        var matches = registry.GetMatchingHooks("agent:code-reviewer:tool-use:before", HookTiming.Before);

        matches.Should().HaveCount(3);
        matches[0].Name.Should().Be("high");
        matches[1].Name.Should().Be("mid");
        matches[2].Name.Should().Be("low");
    }

    [Fact]
    public void GetMatchingHooks_FiltersNonMatching()
    {
        var registry = new HookRegistry();
        registry.Register(new TestHook("match", "agent:*:*:before", HookTiming.Before));
        registry.Register(new TestHook("nomatch", "scheduler:*:*:before", HookTiming.Before));

        var matches = registry.GetMatchingHooks("agent:code:tool:before", HookTiming.Before);
        matches.Should().HaveCount(1);
        matches[0].Name.Should().Be("match");
    }

    [Fact]
    public void GetMatchingHooks_FiltersByTiming()
    {
        var registry = new HookRegistry();
        registry.Register(new TestHook("before", "agent:*:*:before", HookTiming.Before));
        registry.Register(new TestHook("after", "agent:*:*:after", HookTiming.After));

        var matches = registry.GetMatchingHooks("agent:test:action:before", HookTiming.Before);
        matches.Should().HaveCount(1);
        matches[0].Name.Should().Be("before");
    }

    [Fact]
    public void Register_IsThreadSafe()
    {
        var registry = new HookRegistry();
        Parallel.For(0, 100, i =>
        {
            registry.Register(new TestHook($"hook-{i}", "agent:*:*:before", HookTiming.Before));
        });

        registry.Count.Should().Be(100);
    }
}
