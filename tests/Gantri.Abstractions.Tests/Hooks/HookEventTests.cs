using Gantri.Abstractions.Hooks;

namespace Gantri.Abstractions.Tests.Hooks;

public class HookEventTests
{
    [Fact]
    public void Parse_ValidPattern_ReturnsHookEvent()
    {
        var evt = HookEvent.Parse("agent:code-reviewer:tool-use:before");

        evt.Domain.Should().Be("agent");
        evt.Component.Should().Be("code-reviewer");
        evt.Action.Should().Be("tool-use");
        evt.Timing.Should().Be(HookTiming.Before);
    }

    [Fact]
    public void Pattern_ReturnsFormattedString()
    {
        var evt = new HookEvent("scheduler", "tickerq", "job-start", HookTiming.After);

        evt.Pattern.Should().Be("scheduler:tickerq:job-start:after");
    }

    [Theory]
    [InlineData("before", HookTiming.Before)]
    [InlineData("after", HookTiming.After)]
    [InlineData("onerror", HookTiming.OnError)]
    [InlineData("around", HookTiming.Around)]
    public void Parse_AllTimings_AreCaseInsensitive(string timing, HookTiming expected)
    {
        var evt = HookEvent.Parse($"domain:comp:action:{timing}");
        evt.Timing.Should().Be(expected);
    }

    [Fact]
    public void Parse_InvalidPattern_TooFewSegments_Throws()
    {
        var act = () => HookEvent.Parse("only:two");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_InvalidTiming_Throws()
    {
        var act = () => HookEvent.Parse("a:b:c:invalid");
        act.Should().Throw<ArgumentException>();
    }
}
