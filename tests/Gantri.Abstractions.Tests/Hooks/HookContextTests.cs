using Gantri.Abstractions.Hooks;

namespace Gantri.Abstractions.Tests.Hooks;

public class HookContextTests
{
    private static HookContext CreateContext() =>
        new(new HookEvent("test", "comp", "action", HookTiming.Before));

    [Fact]
    public void PropertyBag_SetAndGet_Works()
    {
        var ctx = CreateContext();
        ctx.Set("key1", "value1");
        ctx.Set("count", 42);

        ctx.Get<string>("key1").Should().Be("value1");
        ctx.Get<int>("count").Should().Be(42);
    }

    [Fact]
    public void PropertyBag_Get_MissingKey_ReturnsDefault()
    {
        var ctx = CreateContext();
        ctx.Get<string>("missing").Should().BeNull();
        ctx.Get<int>("missing").Should().Be(0);
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsTrue()
    {
        var ctx = CreateContext();
        ctx.Set("key", "val");

        ctx.TryGet<string>("key", out var value).Should().BeTrue();
        value.Should().Be("val");
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var ctx = CreateContext();
        ctx.TryGet<string>("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Has_ExistingKey_ReturnsTrue()
    {
        var ctx = CreateContext();
        ctx.Set("key", "val");
        ctx.Has("key").Should().BeTrue();
    }

    [Fact]
    public void Has_MissingKey_ReturnsFalse()
    {
        var ctx = CreateContext();
        ctx.Has("missing").Should().BeFalse();
    }

    [Fact]
    public void Cancel_SetsCancelledState()
    {
        var ctx = CreateContext();
        ctx.IsCancelled.Should().BeFalse();

        ctx.Cancel("test reason");

        ctx.IsCancelled.Should().BeTrue();
        ctx.CancellationReason.Should().Be("test reason");
    }

    [Fact]
    public void Properties_ReturnsSnapshot()
    {
        var ctx = CreateContext();
        ctx.Set("a", 1);
        ctx.Set("b", "two");

        var props = ctx.Properties;
        props.Should().HaveCount(2);
        props["a"].Should().Be(1);
        props["b"].Should().Be("two");
    }
}
