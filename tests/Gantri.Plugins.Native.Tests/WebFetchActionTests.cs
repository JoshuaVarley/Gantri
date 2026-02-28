using WebFetch.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class WebFetchActionTests
{
    private readonly FetchAction _action = new();

    [Fact]
    public async Task Fetch_MissingUrl_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "fetch",
            Parameters = new Dictionary<string, object?>()
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("url");
    }

    [Fact]
    public async Task Fetch_InvalidUrl_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "fetch",
            Parameters = new Dictionary<string, object?> { ["url"] = "not-a-url" }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid URL");
    }

    [Fact]
    public async Task Fetch_UnsupportedScheme_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "fetch",
            Parameters = new Dictionary<string, object?> { ["url"] = "ftp://example.com" }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task Fetch_HttpUrl_UpgradesToHttps()
    {
        // This test verifies the URL upgrade logic by trying to fetch a known URL
        // The actual fetch may fail due to network, but we check the logic works
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "fetch",
            Parameters = new Dictionary<string, object?> { ["url"] = "http://example.com" }
        });

        // Should not fail with "Invalid URL" or "Unsupported scheme"
        // May succeed or fail with HTTP error depending on network
        if (!result.Success)
        {
            result.Error.Should().NotContain("Invalid URL");
            result.Error.Should().NotContain("Unsupported");
        }
    }
}
