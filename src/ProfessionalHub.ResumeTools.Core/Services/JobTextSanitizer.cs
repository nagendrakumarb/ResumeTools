using System.Text.RegularExpressions;

namespace ProfessionalHub.ResumeTools.Services;

public static partial class JobTextSanitizer
{
    /// <summary>
    /// Sanitizes job description text by stripping non-relevant sections and normalizing whitespace.
    /// Acts as an alias wrapper for pipeline compatibility.
    /// </summary>
    public static string Sanitize(string input) => RelevantDescription(input);

    /// <summary>
    /// Extracts the core job requirements and responsibilities, stripping header/footer noise.
    /// </summary>
    public static string RelevantDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Replace("\r\n", "\n").Replace('\r', '\n');

        var start = RelevantStartRegex().Match(text);
        if (start.Success)
            text = text[start.Index..];

        var end = NonRequirementStartRegex().Match(text);
        if (end.Success)
            text = text[..end.Index];

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"(?im)^\s*(about the job|about this role|the role|what you(?:'|’)ll do)\s*:?\s*$")]
    private static partial Regex RelevantStartRegex();

    [GeneratedRegex(@"(?im)^\s*(travel|what we offer|compensation|salary|benefits|equal opportunity|to apply)\s*:?\s*$")]
    private static partial Regex NonRequirementStartRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}