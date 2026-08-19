using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ProfessionalHub.ResumeTools.Models;

public class ResumeCorrectionService
{
    private static readonly HttpClient httpClient = new HttpClient();

    // Official LanguageTool API endpoint (No proxy needed!)
    private const string LanguageToolUrl = "https://api.languagetoolplus.com/v2/check";

    public async Task<HashSet<string>> ProcessTermsAsync(List<string> incomingTerms, ResumeFixOptions options)
    {
        var termsToAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (incomingTerms is null || options is null)
        {
            return termsToAdd;
        }

        foreach (var term in incomingTerms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            string processedTerm = options.FixGrammarAndSyntax ? await FixSpellingAsync(term) : term.Trim();
            termsToAdd.Add(processedTerm);
        }

        return termsToAdd;
    }

    private async Task<string> FixSpellingAsync(string term)
    {
        try
        {
            var parameters = new Dictionary<string, string>
            {
                { "text", term },
                { "language", "en-US" }
            };

            var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            var response = await httpClient.PostAsync(LanguageToolUrl, content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                return ApplyFirstSuggestion(term, jsonResponse);
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[LanguageTool API Error] Status: {response.StatusCode}, Content: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API Exception] Failed to fix spelling for term '{term}': {ex.Message}");
        }

        return term.Trim();
    }

    public async Task<string> CorrectParagraphGrammarAsync(string paragraphText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(paragraphText))
            {
                return paragraphText;
            }

            var parameters = new Dictionary<string, string>
            {
                { "text", paragraphText },
                { "language", "en-US" }
            };

            var content = new FormUrlEncodedContent(parameters);

            httpClient.DefaultRequestHeaders.Clear();
            var response = await httpClient.PostAsync(LanguageToolUrl, content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                return ApplyFirstSuggestion(paragraphText, jsonResponse);
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[Paragraph API Error] Status: {response.StatusCode}, Content: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Paragraph Exception] Failed to check paragraph grammar: {ex.Message}");
        }

        return paragraphText;
    }

    private string ApplyFirstSuggestion(string originalTerm, string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            if (!root.TryGetProperty("matches", out var matches) || matches.GetArrayLength() == 0)
            {
                return originalTerm.Trim();
            }

            string modifiedTerm = originalTerm;
            int cumulativeOffsetShift = 0;

            // LanguageTool returns matches sorted by their offset ascending.
            // We iterate and adjust indices based on previous replacements length changes.
            foreach (var match in matches.EnumerateArray())
            {
                int offset = match.GetProperty("offset").GetInt32();
                int length = match.GetProperty("length").GetInt32();

                if (match.TryGetProperty("replacements", out var replacements) && replacements.GetArrayLength() > 0)
                {
                    string firstSuggestion = replacements[0].GetProperty("value").GetString();

                    if (!string.IsNullOrEmpty(firstSuggestion))
                    {
                        // Adjust offset based on prior length shifts in the string
                        int adjustedOffset = offset + cumulativeOffsetShift;

                        if (adjustedOffset >= 0 && adjustedOffset + length <= modifiedTerm.Length)
                        {
                            // Replace strictly at the exact index range using slicing
                            modifiedTerm = modifiedTerm.Remove(adjustedOffset, length).Insert(adjustedOffset, firstSuggestion);

                            // Track how much the string length changed for subsequent matches
                            cumulativeOffsetShift += firstSuggestion.Length - length;
                        }
                    }
                }
            }

            return modifiedTerm.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JSON Parsing Exception]: {ex.Message}");
            return originalTerm.Trim();
        }
    }
}