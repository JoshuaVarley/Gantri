using Gantri.Abstractions.Plugins;

namespace Gantri.Abstractions.Tests.Plugins;

public class PluginActionResultTests
{
    [Fact]
    public void Ok_CreatesSuccessResult()
    {
        var result = PluginActionResult.Ok("hello");
        result.Success.Should().BeTrue();
        result.Output.Should().Be("hello");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var result = PluginActionResult.Fail("something broke");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("something broke");
        result.Output.Should().BeNull();
    }
}
