using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Native.Tests;

public class NativePluginValidatorTests
{
    [Fact]
    public void Validate_WrongType_ReportsError()
    {
        var validator = new NativePluginValidator();
        var manifest = new PluginManifest { Type = PluginType.Wasm, Name = "test" };

        var result = validator.Validate(typeof(NativePluginValidatorTests).Assembly, manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Native"));
    }
}
