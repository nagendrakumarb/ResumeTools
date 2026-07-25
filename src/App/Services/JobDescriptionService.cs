using System.Net;
using System.Text.RegularExpressions;

namespace ProfessionalHub.ResumeTools.Services;

public sealed partial class JobDescriptionService(HttpClient httpClient)
{
    public async Task<string> ReadFromUrlAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter a complete http:// or https:// job-posting URL.");

        string content;
        try { content = await httpClient.GetStringAsync(uri, cancellationToken); }
        catch (HttpRequestException)
        {
            var readerUri = new Uri($"https://r.jina.ai/{uri.AbsoluteUri}");
            content = await httpClient.GetStringAsync(readerUri, cancellationToken);
        }

        var text = content.Contains('<')
            ? WebUtility.HtmlDecode(WhitespaceRegex().Replace(TagRegex().Replace(content, " "), " "))
            : content;
        if (text.Length < 100) throw new InvalidDataException("The URL did not return enough readable job-description text. Paste the description instead.");
        return text.Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>|<[^>]+>", RegexOptions.IgnoreCase)] private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex WhitespaceRegex();
}
