using System.Text.RegularExpressions;
using Gantri.Plugins.Sdk;

namespace WebFetch.Plugin;

public sealed partial class FetchAction : ISdkPluginAction
{
    public string ActionName => "fetch";
    public string Description => "Fetch content from a URL, stripping HTML tags for clean text output";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("url", out var urlObj) || urlObj is not string url || string.IsNullOrWhiteSpace(url))
            return ActionResult.Fail("Missing required parameter: url");

        var maxLength = 50000;
        if (context.Parameters.TryGetValue("max_length", out var maxObj))
        {
            maxLength = maxObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 50000
            };
        }
        if (maxLength < 1) maxLength = 50000;

        // Auto-upgrade HTTP to HTTPS
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url[7..];

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return ActionResult.Fail($"Invalid URL: {url}");

        if (uri.Scheme != "https" && uri.Scheme != "http")
            return ActionResult.Fail($"Unsupported URL scheme: {uri.Scheme}");

        try
        {
            var response = await HttpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Strip HTML tags if content looks like HTML
            if (content.Contains('<') && content.Contains('>'))
            {
                content = StripHtmlTags(content);
            }

            // Truncate if needed
            if (content.Length > maxLength)
                content = content[..maxLength] + "\n... [content truncated]";

            return ActionResult.Ok(content);
        }
        catch (HttpRequestException ex)
        {
            return ActionResult.Fail($"HTTP error fetching {url}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ActionResult.Fail($"Request timed out fetching {url}");
        }
    }

    private static string StripHtmlTags(string html)
    {
        // Remove script and style blocks entirely
        html = ScriptStyleRegex().Replace(html, " ");
        // Remove HTML tags
        html = HtmlTagRegex().Replace(html, " ");
        // Decode common HTML entities
        html = html.Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&#39;", "'")
                    .Replace("&nbsp;", " ");
        // Collapse whitespace
        html = WhitespaceRegex().Replace(html, " ").Trim();
        return html;
    }

    [GeneratedRegex(@"<(script|style)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}
