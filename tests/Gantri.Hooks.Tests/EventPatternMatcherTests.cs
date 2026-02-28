namespace Gantri.Hooks.Tests;

public class EventPatternMatcherTests
{
    [Theory]
    [InlineData("agent:code-reviewer:tool-use:before", "agent:code-reviewer:tool-use:before", true)]
    [InlineData("agent:code-reviewer:tool-use:before", "agent:*:tool-use:before", true)]
    [InlineData("agent:code-reviewer:tool-use:before", "*:*:*:before", true)]
    [InlineData("agent:code-reviewer:tool-use:before", "*:*:*:*", true)]
    [InlineData("agent:code-reviewer:tool-use:before", "agent:code-reviewer:tool-use:after", false)]
    [InlineData("agent:code-reviewer:tool-use:before", "scheduler:*:*:before", false)]
    [InlineData("agent:code-reviewer:tool-use:before", "agent:other:tool-use:before", false)]
    public void Matches_VariousPatterns(string eventPattern, string filterPattern, bool expected)
    {
        EventPatternMatcher.Matches(eventPattern, filterPattern).Should().Be(expected);
    }

    [Fact]
    public void Matches_DifferentSegmentCount_ReturnsFalse()
    {
        EventPatternMatcher.Matches("a:b:c:d", "a:b:c").Should().BeFalse();
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        EventPatternMatcher.Matches("Agent:Code:Tool:Before", "agent:code:tool:before").Should().BeTrue();
    }
}
