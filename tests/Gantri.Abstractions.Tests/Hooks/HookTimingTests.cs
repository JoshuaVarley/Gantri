using Gantri.Abstractions.Hooks;

namespace Gantri.Abstractions.Tests.Hooks;

public class HookTimingTests
{
    [Fact]
    public void AllTimingValues_AreDefined()
    {
        var values = Enum.GetValues<HookTiming>();
        values.Should().HaveCount(4);
        values.Should().Contain(HookTiming.Before);
        values.Should().Contain(HookTiming.After);
        values.Should().Contain(HookTiming.OnError);
        values.Should().Contain(HookTiming.Around);
    }
}
