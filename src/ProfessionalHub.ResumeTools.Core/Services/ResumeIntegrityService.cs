using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

/// <summary>
/// Builds a format-neutral inventory before generation and verifies the generated
/// DOCX afterwards. It does not edit documents and therefore cannot disturb the
/// existing correction engine.
/// </summary>
public sealed partial class ResumeIntegrityService
{
    private static readonly IReadOnlyDictionary<ResumeSectionKind, string[]> HeadingAliases =
        new Dictionary<ResumeSectionKind, string[]>
        {
            [ResumeSectionKind.ProfessionalSummary] = ["summary", "professional summary", "profile", "career profile", "objective"],
            [ResumeSectionKind.Skills] = ["skills", "technical skills", "core competencies", "core strengths", "expertise"],
            [ResumeSectionKind.WorkExperience] = ["experience", "professional experience", "work experience", "employment", "career history"],
            [ResumeSectionKind.Projects] = ["projects", "project highlights", "selected projects"],
            [ResumeSectionKind.Education] = ["education", "academic qualifications", "academics"],
            [ResumeSectionKind.Certifications] = ["certifications", "certificates", "licenses"],
            [ResumeSectionKind.Achievements] = ["achievements", "awards", "accomplishments"]
        };

    public ResumeFactInventory CreateSourceInventory(ParsedResume resume)
    {
        ArgumentNullException.ThrowIfNull(resume);

        var blocks = resume.FileType.Equals("DOCX", StringComparison.OrdinalIgnoreCase)
            ? ReadDocxBlocks(resume.OriginalBytes)
            : ReadTextBlocks(resume.Text);

        return BuildInventory(blocks);
    }

    public ResumeFactInventory CreateGeneratedInventory(byte[]? generatedDocx) =>
        BuildInventory(ReadDocxBlocks(generatedDocx));

    public ResumeIntegrityResult Compare(ResumeFactInventory source, ResumeFactInventory generated)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(generated);

        var generatedValues = generated.Facts.Select(f => Normalize(f.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceValues = source.Facts.Select(f => Normalize(f.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preserved = source.Facts.Where(f => generatedValues.Contains(Normalize(f.Value))).ToList();
        var missing = source.Facts.Where(f => !generatedValues.Contains(Normalize(f.Value))).ToList();
        var unsupported = generated.Facts
            .Where(f => IsIdentityFact(f.Kind) && !sourceValues.Contains(Normalize(f.Value)))
            .ToList();
        var generatedSections = generated.Sections
            .Where(s => s.Blocks.Count > 0)
            .Select(s => s.Kind)
            .ToHashSet();
        var missingSections = source.Sections
            .Where(s => s.Kind != ResumeSectionKind.Other && s.Blocks.Count > 0 && !generatedSections.Contains(s.Kind))
            .Select(s => s.Kind)
            .Distinct()
            .ToList();

        return new ResumeIntegrityResult(preserved, missing, unsupported, missingSections);
    }

    public ResumeFixResult Audit(ParsedResume source, byte[] generatedBytes, IReadOnlyList<ResumeFixOutcome> existingOutcomes)
    {
        existingOutcomes ??= Array.Empty<ResumeFixOutcome>();

        if (generatedBytes == null || generatedBytes.Length == 0)
            return new ResumeFixResult(generatedBytes ?? Array.Empty<byte>(), existingOutcomes, ResumeGenerationStatus.GenerationFailed);

        try
        {
            var integrity = Compare(CreateSourceInventory(source), CreateGeneratedInventory(generatedBytes));
            var outcomes = existingOutcomes.ToList();
            outcomes.Add(CreateAuditOutcome(integrity));
            var status = integrity.IsVerified
                ? ResumeGenerationStatus.Verified
                : integrity.UnsupportedFacts.Count > 0 || integrity.MissingSections.Count > 0
                    ? ResumeGenerationStatus.ManualCorrectionRequired
                    : ResumeGenerationStatus.ReviewRecommended;

            return new ResumeFixResult(generatedBytes, outcomes, status, integrity);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException)
        {
            var outcomes = existingOutcomes.Append(new ResumeFixOutcome(
                "Document integrity audit",
                "Manual action required",
                "The generated document remains downloadable, but its fact inventory could not be read: " + ex.Message)).ToList();

            return new ResumeFixResult(generatedBytes, outcomes, ResumeGenerationStatus.ManualCorrectionRequired);
        }
    }

    private static ResumeFixOutcome CreateAuditOutcome(ResumeIntegrityResult result)
    {
        if (result.IsVerified)
            return new ResumeFixOutcome("Document integrity audit", "Verified",
                $"Preserved {result.PreservedFacts.Count} protected identity, date, link, and metric facts; no source section was lost.");

        var details = new List<string>();
        if (result.MissingFacts.Count > 0)
            details.Add($"Review {result.MissingFacts.Count} source fact(s) not found verbatim in the result: {Preview(result.MissingFacts)}");
        if (result.UnsupportedFacts.Count > 0)
            details.Add($"Remove or verify {result.UnsupportedFacts.Count} unsupported identity fact(s): {Preview(result.UnsupportedFacts)}");
        if (result.MissingSections.Count > 0)
            details.Add("Restore missing section(s): " + string.Join(", ", result.MissingSections));

        details.Add("The document is still available to download; use the unchanged original and this audit when correcting it manually.");
        return new ResumeFixOutcome("Document integrity audit", "Manual action required", string.Join(" ", details));
    }

    private static string Preview(IReadOnlyList<ResumeFact> facts) =>
        string.Join(", ", facts.Take(5).Select(f => f.Value.Length > 45 ? f.Value[..45] + "…" : f.Value));

    private static ResumeFactInventory BuildInventory(IReadOnlyList<string> rawBlocks)
    {
        var sections = new List<ResumeSectionInventory>();
        var currentKind = ResumeSectionKind.Contact;
        var currentHeading = "Contact/Header";
        var currentBlocks = new List<string>();

        void Flush()
        {
            if (currentBlocks.Count > 0)
                sections.Add(new ResumeSectionInventory(currentKind, currentHeading, currentBlocks.ToArray()));
            currentBlocks = [];
        }

        foreach (var raw in rawBlocks)
        {
            var block = CollapseWhitespace(raw);
            if (block.Length == 0) continue;
            if (TryClassifyHeading(block, out var kind))
            {
                Flush();
                currentKind = kind;
                currentHeading = block;
                continue;
            }
            currentBlocks.Add(block);
        }
        Flush();

        var facts = new List<ResumeFact>();
        foreach (var section in sections)
            foreach (var block in section.Blocks)
                ExtractFacts(block, section.Kind, facts);

        return new ResumeFactInventory(sections, facts.DistinctBy(f => f.Id).ToList());
    }

    private static void ExtractFacts(string block, ResumeSectionKind section, List<ResumeFact> facts)
    {
        AddMatches(EmailRegex(), "Email", block, section, facts);
        AddMatches(UrlRegex(), "Url", block, section, facts);
        AddMatches(PhoneRegex(), "Phone", block, section, facts, m =>
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            return digits.Length >= 7 ? digits : string.Empty;
        });
        AddMatches(DateRegex(), "Date", block, section, facts);
        AddMatches(MetricRegex(), "Metric", block, section, facts);
    }

    private static void AddMatches(Regex regex, string kind, string block, ResumeSectionKind section,
        List<ResumeFact> facts, Func<Match, string>? valueFactory = null)
    {
        foreach (Match match in regex.Matches(block))
        {
            var value = CollapseWhitespace(valueFactory?.Invoke(match) ?? match.Value).Trim(' ', '.', ',', ';');
            if (value.Length == 0) continue;
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + ":" + Normalize(value))))[..16];
            facts.Add(new ResumeFact(id, value, section, kind));
        }
    }

    private static IReadOnlyList<string> ReadTextBlocks(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> ReadDocxBlocks(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return Array.Empty<string>();

        using var stream = new MemoryStream(bytes, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;

        if (body is null) return Array.Empty<string>();

        return body.Descendants<Paragraph>()
            .Select(p => CollapseWhitespace(p.InnerText))
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static bool TryClassifyHeading(string value, out ResumeSectionKind kind)
    {
        var normalized = Normalize(value.TrimEnd(':'));
        foreach (var pair in HeadingAliases)
        {
            if (pair.Value.Any(alias => normalized.Equals(Normalize(alias), StringComparison.OrdinalIgnoreCase)))
            {
                kind = pair.Key;
                return true;
            }
        }
        kind = ResumeSectionKind.Other;
        return false;
    }

    private static bool IsIdentityFact(string kind) => kind is "Email" or "Phone" or "Url" or "Date";
    private static string Normalize(string value) => NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), "");
    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"\b[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?:https?://|www\.)[^\s|;,]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?:\+?\d[\d\s().-]{7,}\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b(?:(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+)?(?:19|20)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?<![\w.])(?:[$₹€£]\s*)?\d+(?:[.,]\d+)?\s*(?:%|k|m|bn|million|billion|users?|customers?|employees?|hours?|days?|months?|years?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetricRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}+#]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}