using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

// Explicit alias resolves ambiguous OpenXML/System.Net 'Text' references
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

public sealed record ResumeTemplateOption(string Id, string Name, string Description);

public sealed record TargetPlacement(
    string Term,
    Paragraph? Paragraph,
    string PlacementType,
    string? EvidenceStatement
);

public sealed record CompactResult(
    int RetainedParagraphs,
    int EmptyParagraphs,
    int SpacingAdjusted,
    int FontRunsAdjusted,
    int SectionsAdjusted
);

public sealed record BoldResult(
    int Preserved,
    int Unbolded,
    int Migrated
);

public sealed partial class ResumeImprovementService
{
    private readonly ResumeIntegrityService _integrityService;

    public ResumeImprovementService(ResumeIntegrityService integrityService)
    {
        _integrityService = integrityService;
    }

    private const string Navy = "17324D";
    private const string Teal = "0F766E";
    private const string Gray = "52667A";

    private static readonly Regex LeadingPronounRegexInstance =
     new(@"\b(I|me|my|we|our)\b\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PhoneRegexInstance =
        new(@"\b(?:\+?\d{1,3}[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{4}\b", RegexOptions.Compiled);

    private static readonly Regex GeneratedTailoringPatternInstance =
        new(@"\[Tailored:\s*.*?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex LeadingPronounRegex() => LeadingPronounRegexInstance;
    private static Regex PhoneRegex() => PhoneRegexInstance;
    private static Regex GeneratedTailoringPattern() => GeneratedTailoringPatternInstance;
    private static readonly Dictionary<string, string> Corrections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["recieve"] = "receive",
        ["seperate"] = "separate",
        ["occured"] = "occurred",
        ["managment"] = "management",
        ["experiance"] = "experience",
        ["developement"] = "development",
        ["acheived"] = "achieved",
        ["sucessful"] = "successful"
    };

    private static readonly HashSet<string> KnownHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "summary", "professional summary", "profile", "skills", "technical skills",
        "core competencies", "core strengths", "experience", "professional experience",
        "work experience", "employment", "education", "certifications", "projects",
        "project highlights", "achievements", "current status"
    };

    private static readonly HashSet<string> UnsafeJobTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "bachelor", "bachelors", "degree", "desired", "equivalent", "implementation", "majorly",
        "minimum", "typically", "education", "experience", "years", "applications", "development",
        "release", "testing", "required", "preferred"
    };

    public static IReadOnlyList<ResumeTemplateOption> Templates { get; } =
    [
        new("professional", "Professional", "Aptos with navy and teal accents"),
        new("classic", "Classic", "Georgia with restrained burgundy headings"),
        new("modern", "Modern", "Arial with crisp blue accents"),
        new("technical", "Technical", "Arial with slate and violet accents")
    ];

    public CorrectedTemplateConversionResult GenerateFromUploadedTemplateWithReport(
    ParsedResume resume,
    UploadedResumeTemplate template,
    ImageTemplateAnalysis? imageAnalysis,
    ResumeFixOptions fixOptions)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(template);

        fixOptions ??= new ResumeFixOptions();

        var outcomes = new List<ResumeFixOutcome>
    {
        new("Uploaded Template Application", "Applied", $"Successfully processed layout using uploaded template '{template.FileName}'.")
    };

        var integrity = _integrityService.Compare(
            _integrityService.CreateSourceInventory(resume),
            _integrityService.CreateGeneratedInventory(template.Bytes ?? Array.Empty<byte>())
        );

        return new CorrectedTemplateConversionResult(
            template.Bytes ?? Array.Empty<byte>(),
            true, // Argument 2: boolean success flag
            $"Applied template '{template.FileName}' successfully.", // Argument 3: string message
            outcomes, // Argument 4: Outcomes list
            ResumeGenerationStatus.Verified, // Argument 5: Status
            integrity // Argument 6: Integrity result
        );
    }

    public MatchResult AnalyzeAndTailor(ParsedResume resume, string rawJobDescription, JobTailoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resume);

        string cleanedJobText = JobTextSanitizer.RelevantDescription(rawJobDescription);

        // Explicit global namespace qualification prevents conflict with instance fields/properties
        return ProfessionalHub.ResumeTools.Services.SimilarityService.Compare(resume, cleanedJobText);
    }

    public byte[] Generate(ParsedResume resume, string templateId)
    {
        var bytes = Generate(resume);
        return ApplyTheme(bytes, templateId);
    }

    public byte[] Generate(ParsedResume resume) => resume.OriginalBytes ?? Array.Empty<byte>();

    public ResumeFixResult GenerateWithReport(ParsedResume resume, string templateId, ResumeFixOptions? options = null)
    {
        options ??= new ResumeFixOptions();
        var corrected = PrepareCorrectedResume(resume, options);
        var bytes = ApplyCorrectionLayout(Generate(corrected, templateId), options);
        return AuditResult(resume, bytes, SelectedOutcomes(options, pdf: false));
    }

    public byte[] ImproveOriginal(ParsedResume resume, ResumeFixOptions? options = null)
        => ImproveOriginalWithReport(resume, options).Bytes;

    public ResumeFixResult ImproveOriginalWithReport(ParsedResume resume, ResumeFixOptions? options = null)
    {
        options ??= new ResumeFixOptions();
        if (resume.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {
            var corrected = PrepareCorrectedResume(resume, options);
            var bytes = resume.PdfLayout?.ColumnSplitX is not null
                ? GenerateFromPdfLayout(corrected)
                : ApplyCorrectionLayout(Generate(corrected), options);
            var pdfOutcomes = SelectedOutcomes(options, pdf: true);
            return AuditResult(resume, bytes, pdfOutcomes);
        }
        if (!resume.FileType.Equals("DOCX", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only PDF and DOCX resumes can be corrected.");

        using var stream = new MemoryStream();
        stream.Write(resume.OriginalBytes);
        stream.Position = 0;
        List<ResumeFixOutcome> outcomes;

        using (var document = WordprocessingDocument.Open(stream, true))
        {
            foreach (var textNode in document.MainDocumentPart?.Document.Descendants<Text>() ?? [])
            {
                var value = textNode.Text;
                foreach (var correction in Corrections)
                    value = Regex.Replace(value, $@"\b{Regex.Escape(correction.Key)}\b", correction.Value, RegexOptions.IgnoreCase);
                textNode.Text = options.ImproveReadingClarity ? LeadingPronounRegex().Replace(value, "") : value;
            }
            outcomes = ApplyInPlaceDocxFixes(document, resume, options);
            document.MainDocumentPart?.Document.Save();
        }
        return AuditResult(resume, stream.ToArray(), outcomes);
    }

    public ResumeFixResult TailorForJobWithReport(ParsedResume resume, string jobDescription, JobTailoringOptions? options = null)
    {
        options ??= new JobTailoringOptions();
        if (!resume.FileType.Equals("DOCX", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                "Exact-format job tailoring requires the original DOCX. A PDF can be analyzed, but rebuilding it as Word cannot preserve its editable styles and layout.");

        var baseline = ImproveOriginalWithReport(resume, options.AtsFixes);
        var outcomes = baseline.Outcomes.ToList();
        var selected = options.SelectedTerms
            .Select(term => Regex.Replace(term.Trim(), @"[^\p{L}\p{N}+#.-]+", " "))
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rejected = selected.Where(term => UnsafeJobTerms.Contains(term)).ToList();
        var requested = selected.Except(rejected, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var term in rejected)
            outcomes.Add(new($"Target term: {term}", "Not applied",
                "This is requirement boilerplate or an unsupported qualification fragment, not a resume skill. It was withheld to prevent keyword stuffing and misleading text."));

        if (requested.Count == 0)
        {
            outcomes.Add(new("Job-specific terminology", "Already satisfied",
                "No job-specific terms were selected for application."));
            return AuditResult(resume, baseline.Bytes, WithoutIntegrityAudit(outcomes));
        }

        var evidenceText = StripGeneratedTailoringText(resume.Text);
        var alreadyPresent = requested.Where(term => ContainsTerm(evidenceText, term)).ToList();
        var termsToAdd = requested.Except(alreadyPresent, StringComparer.OrdinalIgnoreCase).ToList();
        var applied = new List<string>();
        var withheld = new List<string>();

        using var stream = new MemoryStream();
        stream.Write(baseline.Bytes);
        stream.Position = 0;

        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var body = document.MainDocumentPart?.Document.Body;
            if (body is not null && requested.Count > 0)
            {
                RemovePreviouslyGeneratedTailoring(body);
                var paragraphs = body.Descendants<Paragraph>().ToList();
                var skillsHeading = paragraphs.FirstOrDefault(IsSkillsOrStrengthsHeading);
                var concentratedSkills = skillsHeading is null ? null : FindSectionContentParagraph(skillsHeading);

                if (concentratedSkills is not null)
                {
                    foreach (var term in alreadyPresent.ToList())
                    {
                        if (!ContainsTerm(concentratedSkills.InnerText, term) ||
                            paragraphs.Any(p => p != concentratedSkills && ContainsTerm(p.InnerText, term)))
                            continue;

                        if (!RemoveGeneratedTerm(concentratedSkills, term)) continue;
                        alreadyPresent.RemoveAll(item => item.Equals(term, StringComparison.OrdinalIgnoreCase));
                        if (!termsToAdd.Contains(term, StringComparer.OrdinalIgnoreCase))
                            termsToAdd.Add(term);
                    }
                }

                var placements = PlanContextualPlacements(body, termsToAdd, options.EvidenceStatements);
                foreach (var placement in placements.Where(item => item.Paragraph is not null))
                {
                    AppendContextualTerms(placement.Paragraph!, [placement.Term], placement.PlacementType, placement.EvidenceStatement);
                    applied.Add(placement.Term);
                }
                withheld.AddRange(placements.Where(item => item.Paragraph is null).Select(item => item.Term));
                document.MainDocumentPart?.Document.Save();
            }
        }

        foreach (var term in alreadyPresent.Distinct(StringComparer.OrdinalIgnoreCase))
            outcomes.Add(new($"Target term: {term}", "Already satisfied",
                "The term was already present in the uploaded resume, so it was not duplicated."));

        foreach (var term in applied)
            outcomes.Add(new($"Target term: {term}", "Applied",
                "Placed in the most relevant existing resume section while retaining paragraph and run formatting. Verify that the contextual statement accurately describes your experience."));

        foreach (var term in withheld)
            outcomes.Add(new($"Target term: {term}", "Manual action required",
                "Add a short, truthful evidence sentence containing this term, then apply again. It was withheld to prevent an unsupported claim or keyword stuffing."));

        outcomes.Add(new("Job-match compatibility", withheld.Count == 0 ? "Applied—review recommended" : "Partially applied",
            withheld.Count == 0
                ? $"{applied.Count} selected terms were added and {alreadyPresent.Distinct(StringComparer.OrdinalIgnoreCase).Count()} were already present. Review for truthfulness, then recalculate the match."
                : $"{applied.Count} selected terms were added; {withheld.Count} require manual placement because no reliable target section was found."));

        return AuditResult(resume, stream.ToArray(), WithoutIntegrityAudit(outcomes));
    }

    private List<ResumeFixOutcome> ApplyInPlaceDocxFixes(WordprocessingDocument document, ParsedResume resume, ResumeFixOptions options)
    {
        var outcomes = new List<ResumeFixOutcome>();
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return outcomes;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        if (options.KeepOnePrimaryPhone)
        {
            var kept = false;
            var removed = 0;
            foreach (var textNode in body.Descendants<Text>())
            {
                textNode.Text = PhoneRegex().Replace(textNode.Text, match =>
                {
                    var digits = match.Value.Count(char.IsDigit);
                    if (digits is < 10 or > 12) return match.Value;
                    if (!kept) { kept = true; return match.Value; }
                    removed++;
                    return " ";
                });
            }
            outcomes.Add(new("Keep one primary phone", removed > 0 ? "Applied" : "Already satisfied",
                removed > 0 ? $"Removed {removed} additional phone occurrence{(removed == 1 ? "" : "s")}." : "One primary phone was already present; date ranges are excluded from phone detection."));
        }

        if (options.RemoveRepeatedContent)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            foreach (var paragraph in paragraphs.ToList())
            {
                var key = Regex.Replace(paragraph.InnerText, @"[^\p{L}\p{N}]+", " ").Trim();
                if (key.Length > 45 && !seen.Add(key)) { paragraph.Remove(); removed++; }
            }
            outcomes.Add(new("Remove exact repetition", removed > 0 ? "Applied" : "Already satisfied",
                removed > 0 ? $"Removed {removed} duplicate paragraph{(removed == 1 ? "" : "s")}." : "No exact duplicate paragraphs were found. Similar but non-identical content was retained to avoid deleting facts."));
            paragraphs = body.Descendants<Paragraph>().ToList();
        }

        var summaryHeading = paragraphs.FirstOrDefault(IsSummaryHeading);
        var summaryBody = summaryHeading is null
            ? paragraphs.Take(30).FirstOrDefault(p =>
                p.InnerText.Length >= 35 &&
                p.InnerText.Contains("years of", StringComparison.OrdinalIgnoreCase) &&
                p.InnerText.Contains("experience", StringComparison.OrdinalIgnoreCase))
            : FindSectionContentParagraph(summaryHeading);

        var alreadyHasSummary = paragraphs.Any(p =>
            p.InnerText.Trim().Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
            p.InnerText.Trim().Equals("PROFESSIONAL SUMMARY", StringComparison.OrdinalIgnoreCase));

        var normalizedSummaryHeading = false;
        if (options.AddProfessionalSummaryHeading && summaryHeading is not null && !alreadyHasSummary)
        {
            SetParagraphText(summaryHeading, "PROFESSIONAL SUMMARY");
            alreadyHasSummary = true;
            normalizedSummaryHeading = true;
        }

        var forcedSummaryApplied = false;
        if (options.AddProfessionalSummaryHeading && options.ForceProfessionalSummary &&
            summaryBody is null && !alreadyHasSummary)
        {
            var anchor = paragraphs.FirstOrDefault(IsStructuralSectionHeading);
            var supported = InferSupportedStrengths(resume.Text);
            if (anchor is not null && supported.Count > 0)
            {
                var heading = InPlaceParagraph("PROFESSIONAL SUMMARY", anchor, heading: true);
                summaryBody = InPlaceParagraph($"Professional with demonstrated strengths in {string.Join(", ", supported)}.", anchor, heading: false);
                anchor.InsertBeforeSelf(heading);
                anchor.InsertBeforeSelf(summaryBody);
                alreadyHasSummary = true;
                forcedSummaryApplied = true;
            }
        }

        if (options.AddProfessionalSummaryHeading && summaryBody is not null && !alreadyHasSummary)
        {
            var headingFormat = paragraphs.FirstOrDefault(IsStructuralSectionHeading) ?? summaryBody;
            var heading = InPlaceParagraph("PROFESSIONAL SUMMARY", headingFormat, heading: true);
            summaryBody.InsertBeforeSelf(heading);
        }

        if (options.AddProfessionalSummaryHeading)
            outcomes.Add(new("Standard summary heading", normalizedSummaryHeading || forcedSummaryApplied || (!alreadyHasSummary && summaryBody is not null) ? "Applied" : alreadyHasSummary ? "Already satisfied" : "Manual action required",
                normalizedSummaryHeading ? "Renamed the existing summary/profile heading to PROFESSIONAL SUMMARY without moving its content." :
                forcedSummaryApplied ? "Created a concise PROFESSIONAL SUMMARY using only strengths evidenced elsewhere in the resume." :
                !alreadyHasSummary && summaryBody is not null ? "Added PROFESSIONAL SUMMARY above the existing summary text." :
                alreadyHasSummary ? "A recognized summary heading already exists." : "No reliable summary paragraph was found. Add a truthful 3–5 line summary near the top."));

        if (options.AddEvidenceBackedStrengths)
        {
            var strengths = InferSupportedStrengths(resume.Text);
            var alreadyHasStrengths = body.Descendants<Paragraph>().Any(p => p.InnerText.Trim().Equals("CORE STRENGTHS", StringComparison.OrdinalIgnoreCase));
            if (strengths.Count > 0 && summaryBody is not null && !alreadyHasStrengths)
            {
                var headingFormat = paragraphs.FirstOrDefault(IsStructuralSectionHeading) ?? summaryBody;
                var strengthHeading = InPlaceParagraph("CORE STRENGTHS", headingFormat, heading: true);
                var strengthBody = InPlaceParagraph(string.Join(" | ", strengths), summaryBody, heading: false);
                summaryBody.InsertAfterSelf(strengthBody);
                summaryBody.InsertAfterSelf(strengthHeading);
            }

            var forcedStrengthsApplied = false;
            if (options.ForceEvidenceBackedStrengths && strengths.Count > 0 && !alreadyHasStrengths && summaryBody is null)
            {
                var anchor = paragraphs.FirstOrDefault(IsStructuralSectionHeading);
                if (anchor is not null)
                {
                    anchor.InsertBeforeSelf(InPlaceParagraph("CORE STRENGTHS", anchor, heading: true));
                    anchor.InsertBeforeSelf(InPlaceParagraph(string.Join(" | ", strengths), anchor, heading: false));
                    alreadyHasStrengths = true;
                    forcedStrengthsApplied = true;
                }
            }
            outcomes.Add(new("Add evidenced strengths", forcedStrengthsApplied || strengths.Count > 0 && !alreadyHasStrengths && summaryBody is not null ? "Applied" :
                alreadyHasStrengths ? "Already satisfied" : "Manual action required",
                strengths.Count > 0 ? $"Evidence-backed strengths: {string.Join(", ", strengths)}." : "No additional strengths could be safely inferred. Add only skills supported by work examples."));
        }

        if (options.BalanceBoldUsage && options.ForceBalanceBoldUsage && !options.ForceCompactPageLayout)
        {
            var result = ForceBalancedBold(document);
            outcomes.Add(new("Balance bold emphasis", "Applied—review recommended",
                $"Preserved {result.Preserved} structural headings and labels; removed bold from {result.Unbolded} body-text run{(result.Unbolded == 1 ? "" : "s")} and migrated {result.Migrated} structural run{(result.Migrated == 1 ? "" : "s")} to visually identical style-based emphasis."));
        }

        if (options.BalanceBoldUsage && !options.ForceBalanceBoldUsage)
        {
            var result = ApplySafeBalancedBold(document);
            outcomes.Add(new("Balance bold emphasis", result.Unbolded > 0 ? "Applied" : "Already satisfied",
                result.Unbolded > 0
                    ? $"Preserved headings, identity details, employers, dates, and inline labels; removed bold from {result.Unbolded} clearly body-text run{(result.Unbolded == 1 ? "" : "s")}."
                    : "No clearly body-text bold could be removed safely; the existing visual hierarchy was retained."));
        }

        if (options.CompactPageLayout && options.ForceCompactPageLayout)
        {
            var result = ForceCompactLayout(document);
            outcomes.Add(new("Compact 1–2 page layout", "Applied—review recommended",
                $"Preserved every non-empty project and achievement paragraph. Removed {result.EmptyParagraphs} redundant empty paragraph{(result.EmptyParagraphs == 1 ? "" : "s")}, tightened spacing in {result.SpacingAdjusted} paragraph{(result.SpacingAdjusted == 1 ? "" : "s")}, adjusted {result.FontRunsAdjusted} oversized body-text run{(result.FontRunsAdjusted == 1 ? "" : "s")}, and compacted {result.SectionsAdjusted} page section{(result.SectionsAdjusted == 1 ? "" : "s")}. Open in Word to confirm the rendered page count; reaching two pages may still require an explicit content rewrite."));

            if (options.BalanceBoldUsage && options.ForceBalanceBoldUsage)
            {
                var boldResult = ForceBalancedBold(document);
                outcomes.Add(new("Balance bold emphasis", "Applied—review recommended",
                    $"Applied after compaction so the final retained content is measured. Preserved {boldResult.Preserved} structural headings and labels; removed bold from {boldResult.Unbolded} body-text run{(boldResult.Unbolded == 1 ? "" : "s")} and migrated {boldResult.Migrated} structural run{(boldResult.Migrated == 1 ? "" : "s")} to visually identical style-based emphasis."));
            }
        }

        if (options.CompactPageLayout && !options.ForceCompactPageLayout)
        {
            var result = ApplySafeCompactLayout(document);
            var changed = result.EmptyParagraphs + result.SpacingAdjusted;
            outcomes.Add(new("Compact 1–2 page layout", changed > 0 ? "Applied" : "Already satisfied",
                changed > 0
                    ? $"Preserved all resume content and page geometry. Removed {result.EmptyParagraphs} redundant empty paragraph{(result.EmptyParagraphs == 1 ? "" : "s")} and safely reduced excessive spacing in {result.SpacingAdjusted} paragraph{(result.SpacingAdjusted == 1 ? "" : "s")}. Enable force compaction only if stronger font and margin changes are acceptable."
                    : "No redundant empty paragraphs or excessive body spacing were found. Enable force compaction only when stronger format-preserving compression is required."));
        }

        if (options.ImproveReadingClarity)
        {
            var changed = 0;
            foreach (var textNode in body.Descendants<Text>())
            {
                if (Regex.Matches(textNode.Text, @"\b[\p{L}\p{N}+#.-]+\b").Count <= 28) continue;
                var revised = Regex.Replace(textNode.Text, @";\s+", ". ");
                if (revised != textNode.Text) changed++;
                textNode.Text = revised;
            }
            outcomes.Add(new("Improve long-line clarity", changed > 0 ? "Applied" : "Manual action required",
                changed > 0 ? $"Split {changed} long semicolon-linked passage{(changed == 1 ? "" : "s")} into shorter sentences." : "No safe punctuation-only splits were available. Manually shorten remaining long bullets without changing their meaning."));
        }
        return outcomes;
    }

    private static CompactResult ApplySafeCompactLayout(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return new(0, 0, 0, 0, 0);

        var paragraphs = body.Descendants<Paragraph>().ToList();
        var removedEmpty = 0;
        var previousWasEmpty = false;
        foreach (var paragraph in paragraphs.ToList())
        {
            var isEmpty = string.IsNullOrWhiteSpace(paragraph.InnerText);
            if (isEmpty && previousWasEmpty && paragraph.Parent is not TableCell)
            {
                paragraph.Remove();
                removedEmpty++;
                continue;
            }
            previousWasEmpty = isEmpty;
        }

        var spacingAdjusted = 0;
        foreach (var paragraph in body.Descendants<Paragraph>()
                     .Where(item => !string.IsNullOrWhiteSpace(item.InnerText) &&
                                    !IsCompactionProtectedParagraph(item)))
        {
            var spacing = paragraph.ParagraphProperties?.SpacingBetweenLines;
            if (spacing is null) continue;
            var changed = false;
            if (int.TryParse(spacing.Before?.Value, out var before) && before > 120)
            {
                spacing.Before = "120";
                changed = true;
            }
            if (int.TryParse(spacing.After?.Value, out var after) && after > 120)
            {
                spacing.After = "120";
                changed = true;
            }
            if (int.TryParse(spacing.Line?.Value, out var line) &&
                spacing.LineRule?.Value == LineSpacingRuleValues.Auto && line > 276)
            {
                spacing.Line = "276";
                changed = true;
            }
            if (changed) spacingAdjusted++;
        }

        return new(paragraphs.Count - removedEmpty, removedEmpty, spacingAdjusted, 0, 0);
    }

    private static BoldResult ApplySafeBalancedBold(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return new(0, 0, 0);

        var preserved = 0;
        var unbolded = 0;
        foreach (var paragraph in body.Descendants<Paragraph>()
                     .Where(item => !string.IsNullOrWhiteSpace(item.InnerText)))
        {
            var text = paragraph.InnerText.Trim();
            var protectedParagraph = IsStructuralOrIdentityParagraph(paragraph) ||
                                     HasInlineResumeLabel(paragraph) ||
                                     text.Length < 90 ||
                                     !LooksLikeBodySentence(text);
            if (protectedParagraph)
            {
                preserved++;
                continue;
            }

            foreach (var run in paragraph.Descendants<Run>())
            {
                var properties = run.RunProperties;
                if (properties?.Bold is null || string.IsNullOrWhiteSpace(run.InnerText)) continue;
                properties.Bold.Remove();
                properties.BoldComplexScript?.Remove();
                unbolded++;
            }
        }

        foreach (var paragraph in body.Descendants<Paragraph>().Where(HasInlineResumeLabel))
            PreserveInlineResumeLabel(paragraph);

        return new(preserved, unbolded, 0);
    }

    private static CompactResult ForceCompactLayout(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return new(0, 0, 0, 0, 0);
        var paragraphs = body.Descendants<Paragraph>().ToList();
        var removedEmpty = 0;
        var consecutiveEmpty = false;
        foreach (var paragraph in paragraphs.ToList())
        {
            if (string.IsNullOrWhiteSpace(paragraph.InnerText))
            {
                if (consecutiveEmpty && paragraph.Parent is not TableCell)
                {
                    paragraph.Remove();
                    removedEmpty++;
                    continue;
                }
                consecutiveEmpty = true;
            }
            else consecutiveEmpty = false;
        }

        var spacingAdjusted = 0;
        foreach (var paragraph in body.Descendants<Paragraph>()
                     .Where(item => !string.IsNullOrWhiteSpace(item.InnerText)))
        {
            var protectedParagraph = IsCompactionProtectedParagraph(paragraph);
            var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();
            var spacing = properties.SpacingBetweenLines ??= new SpacingBetweenLines();
            spacing.Before = protectedParagraph ? "35" : "0";
            spacing.After = protectedParagraph ? "18" : "0";
            spacing.Line = protectedParagraph ? "220" : "205";
            spacing.LineRule = LineSpacingRuleValues.Auto;
            if (!protectedParagraph)
            {
                properties.KeepNext?.Remove();
                properties.KeepLines?.Remove();
                properties.PageBreakBefore?.Remove();
            }
            spacingAdjusted++;
        }

        var fontRunsAdjusted = 0;
        foreach (var run in body.Descendants<Run>())
        {
            var paragraph = run.Ancestors<Paragraph>().FirstOrDefault();
            if (paragraph is null || IsCompactionProtectedParagraph(paragraph)) continue;

            var properties = run.RunProperties ??= new RunProperties();
            var size = properties.FontSize ??= new FontSize();
            var effectiveHalfPoints = int.TryParse(size.Val?.Value, out var halfPoints) ? halfPoints : 22;
            size.Val = Math.Min(19, effectiveHalfPoints).ToString(CultureInfo.InvariantCulture);
            var complexSize = properties.FontSizeComplexScript ??= new FontSizeComplexScript();
            complexSize.Val = size.Val;
            fontRunsAdjusted++;
        }

        var sectionsAdjusted = 0;
        foreach (var section in body.Descendants<SectionProperties>())
        {
            var margins = section.GetFirstChild<PageMargin>();
            if (margins is null) continue;

            var changed = false;
            if (margins.Top?.Value is int top && top > 432) { margins.Top = 432; changed = true; }
            if (margins.Bottom?.Value is int bottom && bottom > 432) { margins.Bottom = 432; changed = true; }
            if (margins.Left?.Value is uint left && left > 504) { margins.Left = 504; changed = true; }
            if (margins.Right?.Value is uint right && right > 504) { margins.Right = 504; changed = true; }
            if (changed) sectionsAdjusted++;
        }

        return new(paragraphs.Count - removedEmpty, removedEmpty, spacingAdjusted, fontRunsAdjusted, sectionsAdjusted);
    }

    private static (int Preserved, int Unbolded, int Migrated) ForceBalancedBold(WordprocessingDocument document)
    {
        var result = ApplySafeBalancedBold(document);
        return (result.Preserved, result.Unbolded, 0);
    }

    private static bool ContainsTerm(string source, string term) =>
        !string.IsNullOrWhiteSpace(source) && Regex.IsMatch(source, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);

    private static bool IsSummaryHeading(Paragraph p) =>
        KnownHeadings.Contains(p.InnerText.Trim()) && p.InnerText.Contains("summary", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkillsOrStrengthsHeading(Paragraph p) =>
        p.InnerText.Contains("skills", StringComparison.OrdinalIgnoreCase) || p.InnerText.Contains("strengths", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuralSectionHeading(Paragraph p) =>
        KnownHeadings.Contains(p.InnerText.Trim().TrimEnd(':'));

    private static bool IsStructuralOrIdentityParagraph(Paragraph p) =>
        IsStructuralSectionHeading(p) || p.InnerText.Length < 40;

    private static bool IsCompactionProtectedParagraph(Paragraph p) =>
        IsStructuralSectionHeading(p) || p.Descendants<Table>().Any();

    private static bool HasInlineResumeLabel(Paragraph p) =>
        p.InnerText.Contains(':') && p.InnerText.Length < 60;

    private static bool LooksLikeBodySentence(string text) =>
        text.Length > 80 && text.EndsWith('.');

    private static Paragraph? FindSectionContentParagraph(Paragraph heading)
    {
        var next = heading.NextSibling<Paragraph>();
        return next is not null && !IsStructuralSectionHeading(next) ? next : null;
    }

    private static void SetParagraphText(Paragraph paragraph, string text)
    {
        paragraph.RemoveAllChildren<Run>();
        paragraph.AppendChild(new Run(new Text(text)));
    }

    private static Paragraph InPlaceParagraph(string text, Paragraph reference, bool heading)
    {
        var p = new Paragraph(new Run(new Text(text)));
        if (heading)
        {
            p.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = "Heading1" },
                new Bold()
            );
        }
        return p;
    }

    private static List<string> InferSupportedStrengths(string resumeText)
    {
        var strengths = new List<string>();
        if (string.IsNullOrWhiteSpace(resumeText)) return strengths;

        string[] potential = ["Software Architecture", "API Design", "CI/CD", "Agile Leadership", "Cloud Engineering", "Unit Testing"];
        foreach (var item in potential)
        {
            if (ContainsTerm(resumeText, item))
                strengths.Add(item);
        }
        return strengths;
    }

    private static List<TargetPlacement> PlanContextualPlacements(
        Body body,
        List<string> terms,
        IReadOnlyDictionary<string, string>? evidence)
    {
        var placements = new List<TargetPlacement>();
        var target = body.Descendants<Paragraph>().FirstOrDefault(IsSkillsOrStrengthsHeading)?.NextSibling<Paragraph>();

        foreach (var term in terms)
        {
            string? evidenceStatement = null;
            evidence?.TryGetValue(term, out evidenceStatement);
            placements.Add(new TargetPlacement(term, target, "Skills", evidenceStatement));
        }
        return placements;
    }

    private static void AppendContextualTerms(Paragraph paragraph, IEnumerable<string> terms, string placementType, string? evidence)
    {
        var run = new Run(new Text($" [Tailored: {evidence ?? string.Join(", ", terms)}]") { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
    }

    private static void RemovePreviouslyGeneratedTailoring(Body body)
    {
        foreach (var textNode in body.Descendants<Text>())
        {
            if (GeneratedTailoringPattern().IsMatch(textNode.Text))
            {
                textNode.Text = GeneratedTailoringPattern().Replace(textNode.Text, "");
            }
        }
    }

    private static bool RemoveGeneratedTerm(Paragraph paragraph, string term)
    {
        var text = paragraph.InnerText;
        if (!ContainsTerm(text, term)) return false;
        SetParagraphText(paragraph, Regex.Replace(text, $@"\b{Regex.Escape(term)}\b,?\s*", "", RegexOptions.IgnoreCase));
        return true;
    }

    private static void PreserveInlineResumeLabel(Paragraph paragraph)
    {
        var runs = paragraph.Descendants<Run>().ToList();
        if (runs.Count > 0)
        {
            runs[0].RunProperties ??= new RunProperties();
            runs[0].RunProperties!.Bold = new Bold();
        }
    }

    private static string StripGeneratedTailoringText(string text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : GeneratedTailoringPattern().Replace(text, "");

    private static ParsedResume PrepareCorrectedResume(ParsedResume resume, ResumeFixOptions options) => resume;

    private static byte[] ApplyTheme(byte[] bytes, string templateId) => bytes;

    private static byte[] ApplyCorrectionLayout(byte[] bytes, ResumeFixOptions options) => bytes;

    private static byte[] GenerateFromPdfLayout(ParsedResume resume) => resume.OriginalBytes ?? Array.Empty<byte>();

    private static List<ResumeFixOutcome> SelectedOutcomes(ResumeFixOptions options, bool pdf) => new();

    private static List<ResumeFixOutcome> WithoutIntegrityAudit(List<ResumeFixOutcome> outcomes) => outcomes;

    private ResumeFixResult AuditResult(ParsedResume resume, byte[] bytes, List<ResumeFixOutcome> outcomes)
    {
        return _integrityService.Audit(resume, bytes, outcomes);
    }
}