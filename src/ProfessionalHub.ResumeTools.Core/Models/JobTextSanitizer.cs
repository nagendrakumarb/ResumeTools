using System;
using System.Text.RegularExpressions;

namespace ProfessionalHub.ResumeTools.Core.Services;

/// <summary>
/// Provides boilerplate and noise removal services for job descriptions.
/// </summary>
public static class JobTextSanitizer
{
    private static readonly Regex EeoAndBoilerplateRegex = new(
        @"(?i)(\b(equal\s+opportunity\s+employer|eeo\s+statement|affirmative\s+action|veteran\s+status|disability\s+status|race,\s*color,\s*religion)\b.*|^\s*about\s+(us|our\s+company)\b.*|^\s*benefits(\s+and\s+perks)?\b.*|^\s*how\s+to\s+apply\b.*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex UrlRegex = new(
        @"https?://\S+|www\.\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NoiseSymbolsRegex = new(
        @"[\*=_~#]{3,}",
        RegexOptions.Compiled);

    private static readonly Regex MultiWhitespaceRegex = new(
        @"[ \t]+",
        RegexOptions.Compiled);

    private static readonly Regex MultiNewlineRegex = new(
        @"(\r?\n){3,}",
        RegexOptions.Compiled);

    /// <summary>
    /// Removes boilerplate headings, EEO text, URLs, contacts, and noise formatting from job text.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Remove URLs and Emails
        string sanitized = UrlRegex.Replace(input, string.Empty);
        sanitized = EmailRegex.Replace(sanitized, string.Empty);

        // 2. Strip Noise formatting characters (e.g., ***, ===)
        sanitized = NoiseSymbolsRegex.Replace(sanitized, " ");

        // 3. Remove known EEO / Standard Boilerplate lines
        sanitized = EeoAndBoilerplateRegex.Replace(sanitized, string.Empty);

        // 4. Clean up spaces and multiple blank lines
        sanitized = MultiWhitespaceRegex.Replace(sanitized, " ");
        sanitized = MultiNewlineRegex.Replace(sanitized, "\n\n");

        return sanitized.Trim();
    }
}
