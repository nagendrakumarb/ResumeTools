using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed record ResumeTemplateOption(string Id, string Name, string Description);

public sealed partial class ResumeImprovementService
{
    private readonly ResumeIntegrityService integrityService = new();
    private const string Navy = "17324D";
    private const string Teal = "0F766E";
    private const string Gray = "52667A";
    private static readonly Dictionary<string, string> Corrections = new(StringComparer.OrdinalIgnoreCase)
    { ["recieve"] = "receive", ["seperate"] = "separate", ["occured"] = "occurred", ["managment"] = "management", ["experiance"] = "experience", ["developement"] = "development", ["acheived"] = "achieved", ["sucessful"] = "successful" };
    private static readonly HashSet<string> KnownHeadings = new(StringComparer.OrdinalIgnoreCase)
    { "summary", "professional summary", "profile", "skills", "technical skills", "core competencies", "core strengths", "experience", "professional experience", "work experience", "employment", "education", "certifications", "projects", "project highlights", "achievements", "current status" };
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

    public byte[] Generate(ParsedResume resume, string templateId)
    {
        var bytes = Generate(resume);
        return ApplyTheme(bytes, templateId);
    }

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
        using var stream = new MemoryStream(); stream.Write(resume.OriginalBytes); stream.Position = 0;
        List<ResumeFixOutcome> outcomes;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            foreach (var text in document.MainDocumentPart?.Document.Descendants<Text>() ?? [])
            {
                var value = text.Text;
                foreach (var correction in Corrections) value = Regex.Replace(value, $@"\b{Regex.Escape(correction.Key)}\b", correction.Value, RegexOptions.IgnoreCase);
                text.Text = options.ImproveReadingClarity ? LeadingPronounRegex().Replace(value, "") : value;
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
        var alreadyPresent = requested
            .Where(term => ContainsTerm(evidenceText, term))
            .ToList();
        var termsToAdd = requested
            .Except(alreadyPresent, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                            paragraphs.Any(paragraph => paragraph != concentratedSkills &&
                                ContainsTerm(paragraph.InnerText, term)))
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

    private ResumeFixResult AuditResult(ParsedResume source, byte[] bytes, IReadOnlyList<ResumeFixOutcome> outcomes) =>
        integrityService.Audit(source, bytes, WithoutIntegrityAudit(outcomes));

    private static IReadOnlyList<ResumeFixOutcome> WithoutIntegrityAudit(IEnumerable<ResumeFixOutcome> outcomes) =>
        outcomes.Where(outcome => !outcome.Name.Equals("Document integrity audit", StringComparison.OrdinalIgnoreCase)).ToList();

    private static string StripGeneratedTailoringText(string value) =>
        GeneratedTailoringPattern.Replace(value, " ");

    private static void RemovePreviouslyGeneratedTailoring(Body body)
    {
        foreach (var text in body.Descendants<Text>())
            text.Text = GeneratedTailoringPattern.Replace(text.Text, "");
    }

    private static List<ResumeFixOutcome> ApplyInPlaceDocxFixes(WordprocessingDocument document, ParsedResume resume, ResumeFixOptions options)
    {
        var outcomes = new List<ResumeFixOutcome>();
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return outcomes;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        if (options.KeepOnePrimaryPhone)
        {
            var kept = false;
            var removed = 0;
            foreach (var text in body.Descendants<Text>())
            {
                text.Text = PhoneRegex().Replace(text.Text, match =>
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
                summaryBody = InPlaceParagraph(
                    $"Professional with demonstrated strengths in {string.Join(", ", supported)}.",
                    anchor, heading: false);
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
            if (options.ForceEvidenceBackedStrengths && strengths.Count > 0 &&
                !alreadyHasStrengths && summaryBody is null)
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
            foreach (var text in body.Descendants<Text>())
            {
                if (Regex.Matches(text.Text, @"\b[\p{L}\p{N}+#.-]+\b").Count <= 28) continue;
                var revised = Regex.Replace(text.Text, @";\s+", ". ");
                if (revised != text.Text) changed++;
                text.Text = revised;
            }
            outcomes.Add(new("Improve long-line clarity", changed > 0 ? "Applied" : "Manual action required",
                changed > 0 ? $"Split {changed} long semicolon-linked passage{(changed == 1 ? "" : "s")} into shorter sentences." : "No safe punctuation-only splits were available. Manually shorten remaining long bullets without changing their meaning."));
        }
        return outcomes;
    }

    private sealed record CompactResult(int Paragraphs, int EmptyParagraphs, int SpacingAdjusted, int FontRunsAdjusted, int SectionsAdjusted);
    private sealed record BoldResult(int Preserved, int Unbolded, int Migrated);

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
            if (paragraph is null || IsCompactionProtectedParagraph(paragraph))
            {
                continue;
            }

            var properties = run.RunProperties ??= new RunProperties();
            var size = properties.FontSize ??= new FontSize();
            var effectiveHalfPoints = int.TryParse(size.Val?.Value, out var halfPoints)
                ? halfPoints
                : 22;
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
            if (margins.Top?.Value is int top && top > 432)
            {
                margins.Top = 432;
                changed = true;
            }
            if (margins.Bottom?.Value is int bottom && bottom > 432)
            {
                margins.Bottom = 432;
                changed = true;
            }
            if (margins.Left?.Value is uint left && left > 504)
            {
                margins.Left = 504;
                changed = true;
            }
            if (margins.Right?.Value is uint right && right > 504)
            {
                margins.Right = 504;
                changed = true;
            }
            if (changed) sectionsAdjusted++;
        }

        return new(
            paragraphs.Count - removedEmpty,
            removedEmpty,
            spacingAdjusted,
            fontRunsAdjusted,
            sectionsAdjusted);
    }

    private static List<Paragraph> BuildCompactionCandidates(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>().Where(p => !string.IsNullOrWhiteSpace(p.InnerText)).ToList();
        var fullyProtectedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "summary", "professional summary", "profile", "skills", "technical skills",
              "core competencies", "core strengths" };
        var section = "";
        var contentIndexInSection = 0;
        var candidates = new List<(Paragraph Paragraph, int Index, bool Quantified)>();
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            if (IsStructuralSectionHeading(paragraph))
            {
                section = paragraph.InnerText.Trim().TrimEnd(':').ToLowerInvariant();
                contentIndexInSection = 0;
                continue;
            }
            contentIndexInSection++;
            var text = paragraph.InnerText.Trim();
            var preserveCoreEducation = (section.Equals("education", StringComparison.OrdinalIgnoreCase) ||
                                         section.Equals("certifications", StringComparison.OrdinalIgnoreCase)) &&
                                        contentIndexInSection <= 2;
            if (fullyProtectedSections.Contains(section) ||
                preserveCoreEducation ||
                IsStructuralOrIdentityParagraph(paragraph) ||
                text.Length < 18 ||
                text.Contains('@') ||
                text.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase))
                continue;
            candidates.Add((paragraph, index, Regex.IsMatch(text, @"\d|%|\$|₹|€|£")));
        }
        return candidates
            .OrderBy(item => item.Quantified) // retain quantified evidence longest
            .ThenByDescending(item => item.Index) // shorten older/later material first
            .Select(item => item.Paragraph)
            .ToList();
    }

    private static int CountWords(string text)
        => Regex.Matches(text, @"[\p{L}\p{N}+#.-]+").Count;

    private static BoldResult ForceBalancedBold(WordprocessingDocument document)
    {
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null) return new(0, 0, 0);
        var paragraphs = body.Descendants<Paragraph>().Where(p => !string.IsNullOrWhiteSpace(p.InnerText)).ToList();
        var preserved = 0;
        var unbolded = 0;
        var structuralParagraphs = new List<Paragraph>();
        foreach (var paragraph in paragraphs)
        {
            if (IsStructuralOrIdentityParagraph(paragraph) ||
                HasInlineResumeLabel(paragraph) ||
                (paragraph.InnerText.Trim().Length < 90 && !LooksLikeBodySentence(paragraph.InnerText)))
            {
                preserved++;
                structuralParagraphs.Add(paragraph);
                continue;
            }
            foreach (var runProperties in paragraph.Descendants<RunProperties>())
            {
                if (runProperties.Bold is not null) { runProperties.Bold.Remove(); unbolded++; }
                runProperties.BoldComplexScript?.Remove();
            }
        }

        foreach (var paragraph in paragraphs.Where(HasInlineResumeLabel))
            PreserveInlineResumeLabel(paragraph);

        var migrated = 0;
        if (DirectBoldPercentage(body) > 18)
        {
            EnsurePreservedBoldStyle(document);
            foreach (var paragraph in structuralParagraphs.AsEnumerable().Reverse())
            {
                if (DirectBoldPercentage(body) <= 18) break;
                foreach (var runProperties in paragraph.Descendants<RunProperties>())
                {
                    if (runProperties.Bold is null) continue;
                    runProperties.Bold.Remove();
                    runProperties.BoldComplexScript?.Remove();
                    runProperties.RunStyle = new RunStyle { Val = "ResumePreservedBold" };
                    migrated++;
                }
            }
        }
        return new(preserved, unbolded, migrated);
    }

    private static bool HasInlineResumeLabel(Paragraph paragraph)
    {
        var text = paragraph.InnerText.TrimStart();
        var delimiter = text.IndexOf(':');
        if (delimiter is > 0 and <= 45) return true;

        var firstLine = text.Split(['\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        return firstLine is { Length: > 0 and <= 55 } &&
               firstLine.Any(char.IsLetter) &&
               firstLine.Where(char.IsLetter).All(char.IsUpper);
    }

    private static void PreserveInlineResumeLabel(Paragraph paragraph)
    {
        var text = paragraph.InnerText;
        var colon = text.IndexOf(':');
        var firstBreak = text.IndexOfAny(['\r', '\n']);
        var boundary = colon is > 0 and <= 45
            ? colon + 1
            : firstBreak is > 0 and <= 55
                ? firstBreak
                : 0;
        if (boundary == 0) return;

        var offset = 0;
        foreach (var run in paragraph.Descendants<Run>())
        {
            var length = run.InnerText.Length;
            if (length > 0 && offset < boundary)
            {
                var properties = run.RunProperties ??= new RunProperties();
                properties.Bold ??= new Bold();
                properties.BoldComplexScript ??= new BoldComplexScript();
            }
            offset += length;
        }
    }

    private static double DirectBoldPercentage(Body body)
    {
        var runs = body.Descendants<Run>().ToList();
        var characters = runs.Sum(run => run.InnerText.Length);
        if (characters == 0) return 0;
        var boldCharacters = runs.Where(run => run.RunProperties?.Bold is not null).Sum(run => run.InnerText.Length);
        return boldCharacters * 100d / characters;
    }

    private static void EnsurePreservedBoldStyle(WordprocessingDocument document)
    {
        var styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles;
        if (styles is null || styles.Elements<Style>().Any(style =>
                style.StyleId?.Value == "ResumePreservedBold")) return;
        styles.Append(new Style(
            new StyleName { Val = "Resume Preserved Bold" },
            new BasedOn { Val = "DefaultParagraphFont" },
            new StyleRunProperties(new Bold()))
        {
            Type = StyleValues.Character,
            StyleId = "ResumePreservedBold",
            CustomStyle = true
        });
        styles.Save();
    }

    private static bool IsStructuralOrIdentityParagraph(Paragraph paragraph)
    {
        var text = paragraph.InnerText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (IsStructuralSectionHeading(paragraph)) return true;
        var style = StyleOf(paragraph);
        if (style.Contains("Title", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Subtitle", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Length <= 55 && (text.EndsWith(':') || DateRegex().IsMatch(text))) return true;
        return paragraph.Parent is Body &&
               paragraph.Parent.Elements<Paragraph>().Where(p => !string.IsNullOrWhiteSpace(p.InnerText)).Take(2).Contains(paragraph);
    }

    private static bool IsCompactionProtectedParagraph(Paragraph paragraph)
    {
        if (IsStructuralOrIdentityParagraph(paragraph)) return true;

        var text = paragraph.InnerText.Trim();
        var style = StyleOf(paragraph);
        if (style.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Job Title", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Position", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Employer", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Company", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Year", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("Header", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasDisplaySize = paragraph.Descendants<Run>()
            .Select(run => run.RunProperties?.FontSize?.Val?.Value)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Any(halfPoints => halfPoints >= 26);
        return hasDisplaySize &&
               (text.Length <= 180 ||
                text.Where(char.IsLetter).All(char.IsUpper));
    }

    private static bool LooksLikeBodySentence(string text)
        => text.Length >= 55 && (text.Contains('.') || text.Contains(';') || text.Contains('•'));

    private static List<ResumeFixOutcome> SelectedOutcomes(ResumeFixOptions options, bool pdf)
    {
        var outcomes = new List<ResumeFixOutcome>();
        void Add(bool selected, string name, string status, string detail) { if (selected) outcomes.Add(new(name, status, detail)); }
        Add(options.AddProfessionalSummaryHeading, "Standard summary heading", "Applied", "Added when the source did not contain a recognized heading.");
        Add(options.KeepOnePrimaryPhone, "Keep one primary phone", "Applied", "Normalized the contact block to one detected primary phone.");
        Add(options.CompactPageLayout, "Compact 1–2 page layout",
            options.ForceCompactPageLayout ? "Applied—review recommended" : "Manual action required",
            options.ForceCompactPageLayout ? "Applied compact typography and margins. Verify the final page count in Word." : "Enable the force flag to alter layout.");
        Add(options.BalanceBoldUsage, "Balance bold emphasis",
            options.ForceBalanceBoldUsage ? "Applied—review recommended" : "Manual action required",
            options.ForceBalanceBoldUsage ? "Reserved bold styling for headings and key labels while reducing body-text bold." : "Enable the force flag to alter bold styling.");
        Add(options.RemoveRepeatedContent, "Remove exact repetition", "Applied", "Removed exact repeated lines while retaining distinct evidence.");
        Add(options.ImproveReadingClarity, "Improve long-line clarity", "Applied—review recommended", "Applied safe punctuation-based splits; manually review technical bullets.");
        Add(options.AddEvidenceBackedStrengths, "Add evidenced strengths", "Applied—review recommended", "Added only strengths supported by phrases already present.");
        if (pdf) outcomes.Add(new("Preserve original PDF formatting", "Manual action required", "A PDF cannot be edited in place in browser-only mode. Upload the original DOCX to retain exact Word formatting."));
        return outcomes;
    }

    private static void AppendParagraphText(Paragraph paragraph, string value)
    {
        var referenceRun = paragraph.Elements<Run>().LastOrDefault() ??
                           paragraph.Descendants<Run>().LastOrDefault();
        var properties = referenceRun?.RunProperties?.CloneNode(true) as RunProperties;
        var run = new Run();
        if (properties is not null) run.Append(properties);
        run.Append(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
    }

    private sealed record TermPlacement(string Term, Paragraph? Paragraph, string PlacementType, string? EvidenceStatement = null);

    private static List<TermPlacement> PlanContextualPlacements(Body body, IReadOnlyCollection<string> terms, IReadOnlyDictionary<string, string> evidenceStatements)
    {
        var paragraphs = body.Descendants<Paragraph>().ToList();
        var skillsHeading = paragraphs.FirstOrDefault(IsSkillsOrStrengthsHeading);
        var skillsParagraph = skillsHeading is null ? null : FindSectionContentParagraph(skillsHeading);
        var summaryHeading = paragraphs.FirstOrDefault(paragraph =>
            paragraph.InnerText.Trim().TrimEnd(':') is var value &&
            (value.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("PROFESSIONAL SUMMARY", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("PROFILE", StringComparison.OrdinalIgnoreCase)));
        var summaryParagraph = summaryHeading is null ? null : FindSectionContentParagraph(summaryHeading);
        var evidenceParagraphs = paragraphs.Where(IsEvidenceParagraph).ToList();
        var placements = new List<TermPlacement>();
        var paragraphLoad = new Dictionary<Paragraph, int>();

        foreach (var term in terms)
        {
            evidenceStatements.TryGetValue(term, out var suppliedEvidence);
            suppliedEvidence = NormalizeEvidenceStatement(suppliedEvidence);
            var hasSuppliedEvidence = IsValidEvidenceStatement(suppliedEvidence, term);
            if (!HasCredibleEvidence(body.InnerText, term) && !hasSuppliedEvidence)
            {
                placements.Add(new(term, null, "unsupported"));
                continue;
            }

            if (hasSuppliedEvidence)
            {
                var target = FindBestEvidenceParagraph(evidenceParagraphs, term, paragraphLoad) ?? summaryParagraph;
                if (target is not null)
                {
                    placements.Add(new(term, target, "user-evidence", suppliedEvidence));
                    paragraphLoad[target] = paragraphLoad.GetValueOrDefault(target) + 1;
                    continue;
                }
            }

            if (IsSummaryCompetency(term) && summaryParagraph is not null)
            {
                placements.Add(new(term, summaryParagraph, "summary"));
                paragraphLoad[summaryParagraph] = paragraphLoad.GetValueOrDefault(summaryParagraph) + 1;
                continue;
            }

            var anchors = ContextAnchors(term);
            var best = evidenceParagraphs
                .Select(paragraph => new
                {
                    Paragraph = paragraph,
                    AnchorScore = anchors.Count(anchor =>
                        paragraph.InnerText.Contains(anchor, StringComparison.OrdinalIgnoreCase)),
                    Load = paragraphLoad.GetValueOrDefault(paragraph)
                })
                .Where(candidate => candidate.AnchorScore > 0)
                .OrderByDescending(candidate => candidate.AnchorScore)
                .ThenBy(candidate => candidate.Load)
                .ThenBy(candidate => candidate.Paragraph.InnerText.Length)
                .FirstOrDefault()?.Paragraph;
            if (best is not null)
            {
                placements.Add(new(term, best, IsTechnicalSkill(term) ? "technology-evidence" : "experience"));
                paragraphLoad[best] = paragraphLoad.GetValueOrDefault(best) + 1;
                continue;
            }

            if (!IsTechnicalSkill(term) && summaryParagraph is not null)
            {
                placements.Add(new(term, summaryParagraph, "summary"));
                paragraphLoad[summaryParagraph] = paragraphLoad.GetValueOrDefault(summaryParagraph) + 1;
                continue;
            }

            placements.Add(IsTechnicalSkill(term)
                ? new(term, skillsParagraph, "skills")
                : new(term, null, "unsupported"));
        }
        return placements;
    }

    private static Paragraph? FindBestEvidenceParagraph(IReadOnlyCollection<Paragraph> evidenceParagraphs, string term, IReadOnlyDictionary<Paragraph, int> paragraphLoad)
    {
        var anchors = ContextAnchors(term);
        return evidenceParagraphs.Select(paragraph => new
        {
            Paragraph = paragraph,
            AnchorScore = anchors.Count(anchor => paragraph.InnerText.Contains(anchor, StringComparison.OrdinalIgnoreCase)),
            Load = paragraphLoad.GetValueOrDefault(paragraph)
        })
            .OrderByDescending(candidate => candidate.AnchorScore)
            .ThenBy(candidate => candidate.Load)
            .ThenBy(candidate => candidate.Paragraph.InnerText.Length)
            .FirstOrDefault()?.Paragraph;
    }

    private static string? NormalizeEvidenceStatement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.Length <= 360 ? normalized : normalized[..360].TrimEnd();
    }

    private static bool IsValidEvidenceStatement(string? value, string term) =>
        value is { Length: >= 12 } && ContainsTerm(value, term);

    private static bool HasCredibleEvidence(string resumeText, string term)
    {
        if (ContainsTerm(resumeText, term)) return true;
        var aliases = term.ToLowerInvariant() switch
        {
            "rest" or "restful api" => new[] { "web api", "http api", "web service" },
            "ci/cd" => new[] { "continuous integration", "continuous deployment", "build pipeline", "release pipeline" },
            "site reliability" => new[] { "sre", "reliability engineering" },
            "incident response" => new[] { "production incident", "incident remediation" },
            "data engineering" => new[] { "etl pipeline", "data pipeline" },
            "machine learning" => new[] { "ml model", "predictive model" },
            "technical leadership" => new[] { "technical lead", "led engineering", "mentored engineers" },
            "stakeholder management" => new[] { "stakeholder", "client management" },
            "problem solving" => new[] { "problem-solving", "root cause analysis" },
            _ => Array.Empty<string>()
        };
        return aliases.Any(alias => ContainsTerm(resumeText, alias));
    }

    private static bool IsSummaryHeading(Paragraph paragraph)
    {
        var value = paragraph.InnerText.Trim().TrimEnd(':');
        return value.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("PROFESSIONAL SUMMARY", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("PROFILE", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("PROFESSIONAL PROFILE", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("ABOUT ME", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendContextualTerms(Paragraph paragraph, IReadOnlyCollection<string> terms, string placementType, string? evidenceStatement = null)
    {
        if (terms.Count == 0) return;
        var separator = string.IsNullOrWhiteSpace(paragraph.InnerText) ? "" : " ";
        var value = placementType switch
        {
            "user-evidence" => $"{separator}{evidenceStatement}",
            "skills" => $"{separator}| {string.Join(" | ", terms)}",
            "summary" => $"{separator}Demonstrated strengths include {NaturalLanguageList(terms)}.",
            "technology-evidence" => $"{separator}Applied {NaturalLanguageList(terms)} in relevant technical delivery.",
            _ => $"{separator}Applied {NaturalLanguageList(terms)} in relevant professional work."
        };
        AppendParagraphText(paragraph, value);
    }

    private static string NaturalLanguageList(IReadOnlyCollection<string> terms)
    {
        var values = terms.ToArray();
        return values.Length switch
        {
            0 => "",
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values[..^1])}, and {values[^1]}"
        };
    }

    private static bool ContainsTerm(string text, string term)
    {
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool RemoveGeneratedTerm(Paragraph paragraph, string term)
    {
        var items = paragraph.InnerText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var remaining = items.Where(item => !item.Equals(term, StringComparison.OrdinalIgnoreCase)).ToList();
        if (remaining.Count == items.Length) return false;
        SetParagraphText(paragraph, string.Join(" | ", remaining));
        return true;
    }

    private static void SetParagraphText(Paragraph paragraph, string value)
    {
        var properties = paragraph.Descendants<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties;
        paragraph.RemoveAllChildren<Run>();
        var run = new Run();
        if (properties is not null) run.Append(properties);
        run.Append(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
    }

    private static bool IsEvidenceParagraph(Paragraph paragraph)
    {
        var value = paragraph.InnerText.Trim();
        return value.Length is >= 45 and <= 700 &&
               !IsStructuralSectionHeading(paragraph) &&
               !EmailRegex().IsMatch(value) &&
               !PhoneRegex().IsMatch(value);
    }

    private static bool IsTechnicalSkill(string term)
    {
        var value = term.ToLowerInvariant();
        string[] skills =
        [
            ".net", "c#", "f#", "java", "kotlin", "golang", "go", "python", "javascript", "typescript",
            "node.js", "react", "angular", "vue", "blazor", "maui", "sql", "postgresql", "mysql", "oracle",
            "mongodb", "cosmos db", "redis", "elasticsearch", "azure", "aws", "gcp", "docker", "kubernetes",
            "terraform", "ansible", "jenkins", "github actions", "azure devops", "ci/cd", "git", "graphql",
            "grpc", "rest", "restful api", "web api", "api design", "dapper", "spring boot", "oauth", "jwt", "saml", "machine learning", "data science",
            "data engineering", "backend", "frontend", "full stack", "cloud"
        ];
        return skills.Any(skill => value.Equals(skill, StringComparison.OrdinalIgnoreCase) ||
                                   value.Contains(skill, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSummaryCompetency(string term)
    {
        var value = term.ToLowerInvariant();
        return new[] { "communication", "technical leadership", "mentoring", "stakeholder management", "problem solving" }
            .Any(value.Contains);
    }

    private static string[] ContextAnchors(string term)
    {
        var value = term.ToLowerInvariant();
        if (value.Contains("incident") || value.Contains("reliability") || value.Contains("availability"))
            return ["incident", "exception", "monitor", "reliab", "audit", "tracking", "production"];
        if (value.Contains("security") || value.Contains("devsecops"))
            return ["security", "oauth", "jwt", "token", "compliance", "authorization"];
        if (value.Contains("distributed") || value.Contains("microservice") || value.Contains("architecture") || value.Contains("system design"))
            return ["microservice", "service", "api", "messaging", "event", "architecture", "integration"];
        if (value.Contains("machine learning") || value.Contains("data science") || value.Contains("data engineering"))
            return ["data", "analysis", "sql", "model", "etl", "analytics"];
        if (value.Contains("backend"))
            return ["backend", "api", "service", "database", "sql"];
        if (value.Contains("frontend"))
            return ["frontend", "javascript", "ui", "web", "mvc"];
        if (value.Equals("rest", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("restful", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("web api", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("api design", StringComparison.OrdinalIgnoreCase))
            return ["api", "web service", "integration", "endpoint", "http", "service"];
        if (value.Contains("react", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("angular", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("vue", StringComparison.OrdinalIgnoreCase))
            return ["frontend", "javascript", "typescript", "ui", "web", "spa"];
        if (value.Contains("full stack", StringComparison.OrdinalIgnoreCase))
            return ["frontend", "backend", "api", "web", "database", "javascript"];
        if (value.Contains("cloud"))
            return ["azure", "aws", "cloud", "function", "serverless"];
        if (value.Contains("performance") || value.Contains("scalability"))
            return ["performance", "latency", "throughput", "scal", "optimization"];
        return term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsSkillsOrStrengthsHeading(Paragraph paragraph)
    {
        var value = paragraph.InnerText.Trim().TrimEnd(':');
        return value.Equals("CORE STRENGTHS", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("KEY SKILLS", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("SKILLS", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("TECHNICAL SKILLS", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("CORE COMPETENCIES", StringComparison.OrdinalIgnoreCase);
    }

    private static Paragraph? FindSectionContentParagraph(Paragraph heading)
    {
        for (OpenXmlElement? current = heading.NextSibling(); current is not null; current = current.NextSibling())
        {
            if (current is Paragraph paragraph)
            {
                if (IsStructuralSectionHeading(paragraph)) return null;
                if (!string.IsNullOrWhiteSpace(paragraph.InnerText)) return paragraph;
            }
            else if (current is Table table)
            {
                var tableParagraph = table.Descendants<Paragraph>()
                    .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.InnerText));
                if (tableParagraph is not null) return tableParagraph;
            }
        }
        return null;
    }

    private static Paragraph InPlaceParagraph(string text, Paragraph reference, bool heading)
    {
        var properties = heading && IsStructuralSectionHeading(reference)
            ? SafeHeadingProperties(reference)
            : reference.ParagraphProperties?.CloneNode(true) as ParagraphProperties ?? new ParagraphProperties();
        var runProperties = reference.Elements<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties ?? new RunProperties();
        if (heading)
        {
            runProperties.Highlight?.Remove();
            runProperties.Shading?.Remove();
            if (!IsStructuralSectionHeading(reference))
            {
                properties.ParagraphStyleId = new ParagraphStyleId { Val = "Heading3" };
                properties.SpacingBetweenLines = new SpacingBetweenLines { Before = "100", After = "35" };
                runProperties.Bold = new Bold();
            }
        }
        else
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = "Normal" };
            properties.SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "65" };
            runProperties.Bold?.Remove();
            runProperties.BoldComplexScript?.Remove();
        }
        return new Paragraph(properties, new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static ParagraphProperties SafeHeadingProperties(Paragraph reference)
    {
        var source = reference.ParagraphProperties;
        var properties = new ParagraphProperties();
        void Copy(OpenXmlElement? element)
        {
            if (element is not null) properties.Append(element.CloneNode(true));
        }
        Copy(source?.ParagraphStyleId);
        Copy(source?.KeepNext);
        Copy(source?.SpacingBetweenLines);
        Copy(source?.Justification);
        Copy(source?.Indentation);
        Copy(source?.ParagraphBorders);
        Copy(source?.OutlineLevel);
        return properties;
    }

    private static ParsedResume PrepareCorrectedResume(ParsedResume resume, ResumeFixOptions options)
    {
        var primaryPhoneKept = false;
        var normalizedContacts = options.KeepOnePrimaryPhone ? PhoneRegex().Replace(resume.Text, match =>
        {
            var digitCount = match.Value.Count(char.IsDigit);
            if (digitCount is < 10 or > 12) return match.Value;
            if (!primaryPhoneKept) { primaryPhoneKept = true; return match.Value; }
            return " ";
        }) : resume.Text;
        var expanded = Regex.Replace(
            normalizedContacts.Replace("\r", ""),
            @"(?<!^)(?<!\n)(PROFESSIONAL SUMMARY|SUMMARY|SKILLS|EDUCATION|CERTIFICATIONS|WORK EXPERIENCE|PROFESSIONAL EXPERIENCE|EMPLOYMENT|PROJECT HIGHLIGHTS)(?=[A-Z])",
            "\n$1\n",
            RegexOptions.IgnoreCase);
        var source = expanded.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Clean)
            .Where(x => x.Length > 0)
            .ToList();

        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var originalLine in source)
        {
            var line = options.ImproveReadingClarity ? ImproveLineClarity(originalLine) : originalLine;
            var key = Regex.Replace(line, @"[^\p{L}\p{N}]+", " ").Trim();
            if (options.RemoveRepeatedContent && key.Length > 24 && !seen.Add(key)) continue;
            unique.Add(line);
        }

        var hasSummary = unique.Any(x => x.TrimEnd(':').Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                                         x.TrimEnd(':').Equals("PROFESSIONAL SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                                         x.TrimEnd(':').Equals("PROFILE", StringComparison.OrdinalIgnoreCase));
        if (options.AddProfessionalSummaryHeading && !hasSummary)
        {
            var header = ExtractHeader(normalizedContacts, unique);
            var summaryIndex = unique.FindIndex(x => !header.SourceLines.Contains(x) &&
                                                     x.Length >= 90 &&
                                                     !IsHeading(x) &&
                                                     !EmailRegex().IsMatch(x) &&
                                                     !PhoneRegex().IsMatch(x));
            if (summaryIndex >= 0) unique.Insert(summaryIndex, "PROFESSIONAL SUMMARY");
        }

        var strengths = options.AddEvidenceBackedStrengths ? InferSupportedStrengths(normalizedContacts) : [];
        if (strengths.Count > 0 && !unique.Any(x => x.Equals("CORE STRENGTHS", StringComparison.OrdinalIgnoreCase)))
        {
            var skillsIndex = unique.FindIndex(x => x.TrimEnd(':').Equals("SKILLS", StringComparison.OrdinalIgnoreCase) ||
                                                   x.TrimEnd(':').Equals("TECHNICAL SKILLS", StringComparison.OrdinalIgnoreCase));
            var insertAt = skillsIndex >= 0 ? skillsIndex + 1 : Math.Min(unique.Count, 5);
            unique.Insert(insertAt, "CORE STRENGTHS");
            unique.Insert(insertAt + 1, string.Join(" | ", strengths));
        }

        var text = string.Join(Environment.NewLine, unique);
        return resume with { Text = text };
    }

    private static string ImproveLineClarity(string line)
    {
        var words = Regex.Matches(line, @"\b[\p{L}\p{N}+#.-]+\b").Count;
        if (words <= 28) return line;
        return Regex.Replace(line, @";\s+", "." + Environment.NewLine);
    }

    private static List<string> InferSupportedStrengths(string text)
    {
        var strengths = new List<string>();
        void AddWhen(string label, params string[] evidence)
        {
            if (evidence.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase))) strengths.Add(label);
        }
        AddWhen("Project delivery", "project delivery", "delivered", "delivery");
        AddWhen("Technical communication", "documentation", "communication", "requirements");
        AddWhen("Collaboration", "team", "stakeholder", "cross-functional");
        AddWhen("Problem solving", "troubleshoot", "debugging", "problem solving", "exception handling");
        AddWhen("Code review", "code review", "reviewed");
        AddWhen("Adaptability", "adaptation", "adapted", "adjusted");
        return strengths.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private static byte[] ApplyCorrectionLayout(byte[] source, ResumeFixOptions options)
    {
        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            foreach (var style in styles?.Elements<Style>() ?? [])
            {
                var run = style.StyleRunProperties;
                var paragraph = style.StyleParagraphProperties;
                if (run is null || paragraph is null) continue;
                if (options.ForceCompactPageLayout) switch (style.StyleId?.Value)
                    {
                        case "ResumeName":
                            run.FontSize = new FontSize { Val = "48" };
                            paragraph.SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "15", Line = "220", LineRule = LineSpacingRuleValues.Auto };
                            break;
                        case "ResumeSubtitle":
                            run.FontSize = new FontSize { Val = "21" };
                            paragraph.SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "20", Line = "215", LineRule = LineSpacingRuleValues.Auto };
                            break;
                        case "ResumeSection":
                            run.FontSize = new FontSize { Val = "20" };
                            paragraph.SpacingBetweenLines = new SpacingBetweenLines { Before = "85", After = "25", Line = "215", LineRule = LineSpacingRuleValues.Auto };
                            break;
                        case "ResumeRole":
                            run.FontSize = new FontSize { Val = "18" };
                            paragraph.SpacingBetweenLines = new SpacingBetweenLines { Before = "45", After = "10", Line = "210", LineRule = LineSpacingRuleValues.Auto };
                            break;
                        default:
                            run.FontSize = new FontSize { Val = "17" };
                            paragraph.SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "12", Line = "205", LineRule = LineSpacingRuleValues.Auto };
                            if (options.ForceBalanceBoldUsage) run.Bold?.Remove();
                            break;
                    }
            }
            var body = document.MainDocumentPart?.Document.Body;
            var section = body?.Elements<SectionProperties>().LastOrDefault();
            if (section is not null && options.ForceCompactPageLayout)
            {
                section.RemoveAllChildren<PageMargin>();
                section.Append(new PageMargin { Top = 500, Right = 560, Bottom = 500, Left = 560, Header = 180, Footer = 180 });
            }
            styles?.Save();
            document.MainDocumentPart?.Document.Save();
        }
        return stream.ToArray();
    }

    public TemplateConversionResult GenerateFromUploadedTemplate(ParsedResume resume, UploadedResumeTemplate template, ImageTemplateAnalysis? imageAnalysis = null)
    {
        if (template.FileType.Equals("DOCX", StringComparison.OrdinalIgnoreCase))
        {
            using var source = new MemoryStream(); source.Write(template.Bytes); source.Position = 0;
            var populated = false;
            using (var document = WordprocessingDocument.Open(source, true))
            {
                var visibleText = document.MainDocumentPart?.Document?.InnerText ?? "";
                if (visibleText.Contains("{{NAME}}", StringComparison.OrdinalIgnoreCase) || visibleText.Contains("{{RESUME_CONTENT}}", StringComparison.OrdinalIgnoreCase))
                {
                    PopulatePlaceholders(document, resume);
                    document.MainDocumentPart?.Document.Save();
                    populated = true;
                }
            }
            if (populated) return new TemplateConversionResult(source.ToArray(), true, "Template placeholders were populated while retaining the DOCX layout and styles.");
            var themed = ApplyReferenceTheme(Generate(resume), template.Bytes);
            return new TemplateConversionResult(themed, false, "The DOCX had no supported placeholders, so its dominant font and heading colors were applied to the ATS-safe layout.");
        }

        if (template.FileType is "PNG" or "JPG" or "JPEG" or "WEBP")
        {
            var analysis = imageAnalysis ?? new ImageTemplateAnalysis(Teal, "one-column", "none", 0.77);
            var bytes = ApplyImageTheme(Generate(resume), analysis);
            return new TemplateConversionResult(bytes, analysis.Layout.Equals("two-column", StringComparison.OrdinalIgnoreCase), $"Reconstructed the image's {analysis.Layout} layout locally with its {analysis.Sidebar} sidebar, section placement, typography hierarchy, and accent #{analysis.AccentHex}.");
        }

        if (template.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {
            var reconstructed = ApplyPdfReferenceLayout(Generate(resume));
            return new TemplateConversionResult(reconstructed, true, "Reconstructed the uploaded PDF's two-panel visual layout locally in an editable DOCX, including the gray sidebar, central divider, display headings, and dark role banner.");
        }

        return new TemplateConversionResult(Generate(resume), false, "The uploaded template type is not supported. The content was converted with the ATS-safe Professional layout.");
    }

    public CorrectedTemplateConversionResult GenerateFromUploadedTemplateWithReport(
        ParsedResume resume,
        UploadedResumeTemplate template,
        ImageTemplateAnalysis? imageAnalysis = null,
        ResumeFixOptions? options = null)
    {
        options ??= new ResumeFixOptions();
        var corrected = PrepareCorrectedResume(resume, options);
        var conversion = GenerateFromUploadedTemplate(corrected, template, imageAnalysis);
        var bytes = ApplyCorrectionLayout(conversion.Bytes, options);
        var outcomes = SelectedOutcomes(options, pdf: false);
        var message = conversion.Message + " Selected ATS corrections were applied before the content was mapped, and safe layout corrections were applied after conversion.";
        var audited = AuditResult(resume, bytes, outcomes);
        return new CorrectedTemplateConversionResult(
            audited.Bytes,
            conversion.ExactLayout,
            message,
            audited.Outcomes,
            audited.Status,
            audited.Integrity);
    }

    public byte[] Generate(ParsedResume resume)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            AddStyles(main); AddNumbering(main);
            var body = new Body(); main.Document = new Document(body);

            var lines = resume.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Clean).Where(x => x.Length > 0).ToList();
            var header = ExtractHeader(resume.Text, lines);
            body.Append(Paragraph(header.Name, "ResumeName", JustificationValues.Center));
            if (!string.IsNullOrWhiteSpace(header.Role)) body.Append(Paragraph(header.Role, "ResumeSubtitle", JustificationValues.Center));
            if (header.ContactParts.Count > 0) body.Append(Paragraph(string.Join("  |  ", header.ContactParts), "ResumeContact", JustificationValues.Center));
            body.Append(AccentRule());

            foreach (var line in lines.Where(line => !header.SourceLines.Contains(line)))
            {
                var normalized = line.TrimEnd(':').TrimStart('#').Trim();
                if (TrySplitHeading(normalized, out var splitHeading, out var splitContent)) { body.Append(Paragraph(splitHeading.ToUpperInvariant(), "ResumeSection")); if (splitContent.Length > 0) body.Append(Paragraph(splitContent, "ResumeBody")); continue; }
                if (IsHeading(normalized)) { body.Append(Paragraph(normalized.ToUpperInvariant(), "ResumeSection")); continue; }
                if (IsRoleLine(line)) { body.Append(Paragraph(line.TrimStart('#', ' '), "ResumeRole")); continue; }
                if (IsLabelLine(line, out var label, out var detail)) { body.Append(LabelParagraph(label, detail)); continue; }
                body.Append(Paragraph(line, ShouldBullet(line) ? "ResumeBullet" : "ResumeBody", numbering: ShouldBullet(line)));
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 850, Right = 900, Bottom = 850, Left = 900, Header = 360, Footer = 360 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] GenerateFromPdfLayout(ParsedResume resume)
    {
        var layout = resume.PdfLayout ?? throw new InvalidOperationException("PDF layout metadata is required.");
        var split = layout.ColumnSplitX ?? layout.PageWidth * 0.35;
        var leftLines = layout.Lines.Where(line => line.Left + line.Width / 2d < split)
            .OrderBy(line => line.Page).ThenByDescending(line => line.Bottom).ToArray();
        var rightLines = layout.Lines.Where(line => line.Left + line.Width / 2d >= split)
            .OrderBy(line => line.Page).ThenByDescending(line => line.Bottom).ToArray();

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            AddStyles(main); AddNumbering(main);
            var body = new Body(); main.Document = new Document(body);
            var usableWidth = 10440;
            var rightStart = rightLines.Length == 0 ? split : rightLines.Min(line => line.Left);
            var visualColumnRatio = Math.Clamp(rightStart / layout.PageWidth - 0.06, 0.34, 0.36);
            var leftWidth = Math.Clamp((int)Math.Round(usableWidth * visualColumnRatio), 3300, 4700);
            var rightWidth = usableWidth - leftWidth - 260;
            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = usableWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Nil }, new LeftBorder { Val = BorderValues.Nil },
                        new BottomBorder { Val = BorderValues.Nil }, new RightBorder { Val = BorderValues.Nil },
                        new InsideHorizontalBorder { Val = BorderValues.Nil }, new InsideVerticalBorder { Val = BorderValues.Nil })),
                new TableGrid(new GridColumn { Width = leftWidth.ToString() }, new GridColumn { Width = rightWidth.ToString() }));
            var leftCell = PdfLayoutCell(leftWidth, 160);
            var rightCell = PdfLayoutCell(rightWidth, 220);
            AppendPdfLayoutLines(leftCell, leftLines, resume.AverageFontSize, isLeftColumn: true);
            AppendPdfLayoutLines(rightCell, rightLines, resume.AverageFontSize, isLeftColumn: false);
            table.Append(new TableRow(leftCell, rightCell));
            body.Append(table);
            body.Append(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 360, Right = 620, Bottom = 360, Left = 620, Header = 180, Footer = 180 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static TableCell PdfLayoutCell(int width, int padding) => new(
        new TableCellProperties(
            new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa },
            new TableCellMargin(
                new LeftMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa }),
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

    private static void AppendPdfLayoutLines(TableCell cell, IReadOnlyList<PdfLayoutLine> lines, double averageFontSize, bool isLeftColumn)
    {
        var previousPage = lines.Count == 0 ? 1 : lines[0].Page;
        foreach (var line in lines)
        {
            var text = Clean(line.Text);
            if (text.Length == 0) continue;
            var isHeading = line.Bold && (line.FontSize >= Math.Max(averageFontSize * 1.08, 11.5) || text.Length < 36);
            var isName = isLeftColumn && line.Page == 1 && line.Bottom > 0 && line.FontSize >= averageFontSize * 1.35;
            var paragraph = new Paragraph();
            var properties = new ParagraphProperties(
                new SpacingBetweenLines
                {
                    Before = isHeading ? "120" : "20",
                    After = isHeading ? "60" : "20",
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto
                },
                new KeepNext { Val = isHeading });
            if (line.Page != previousPage && isLeftColumn)
                properties.PageBreakBefore = new PageBreakBefore();
            paragraph.Append(properties);
            var runProperties = new RunProperties(
                new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" },
                new FontSize { Val = ((int)Math.Clamp(Math.Round(line.FontSize * 2), 18, isName ? 34 : 28)).ToString() });
            if (line.Bold || isName) runProperties.Bold = new Bold();
            if (isName) runProperties.Color = new Color { Val = "3367E8" };
            paragraph.Append(new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            cell.Append(paragraph);
            previousPage = line.Page;
        }
        if (!cell.Elements<Paragraph>().Any()) cell.Append(new Paragraph());
    }

    private static byte[] ApplyTheme(byte[] source, string templateId)
    {
        var theme = templateId.ToLowerInvariant() switch
        {
            "classic" => (Font: "Georgia", Heading: "7F1D1D", Accent: "A16207"),
            "modern" => (Font: "Arial", Heading: "1D4ED8", Accent: "0891B2"),
            "technical" => (Font: "Arial", Heading: "334155", Accent: "7C3AED"),
            _ => (Font: "Aptos", Heading: Navy, Accent: Teal)
        };
        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            if (styles is not null)
            {
                foreach (var style in styles.Elements<Style>())
                {
                    var run = style.StyleRunProperties;
                    if (run is null) continue;
                    run.RunFonts = new RunFonts { Ascii = theme.Font, HighAnsi = theme.Font };
                    var styleId = style.StyleId?.Value;
                    if (styleId is "ResumeName" or "ResumeSection" or "ResumeRole") run.Color = new Color { Val = theme.Heading };
                    else if (styleId == "ResumeSubtitle") run.Color = new Color { Val = theme.Accent };
                }
                styles.Save();
            }
            var rule = document.MainDocumentPart?.Document.Descendants<BottomBorder>().FirstOrDefault();
            if (rule is not null) rule.Color = theme.Accent;
            ApplyBuiltInLayout(document, templateId, theme.Heading, theme.Accent);
            document.MainDocumentPart?.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] ApplyReferenceTheme(byte[] source, byte[] templateBytes)
    {
        string font = "Aptos", heading = Navy; PageMargin? sourceMargin = null; PageSize? sourceSize = null; uint columnCount = 1; JustificationValues? titleAlignment = null;
        using (var templateStream = new MemoryStream(templateBytes))
        using (var template = WordprocessingDocument.Open(templateStream, false))
        {
            var styles = template.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            font = styles?.Descendants<RunFonts>().Select(x => x.Ascii?.Value).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? font;
            heading = styles?.Elements<Style>().Where(x => (x.StyleId?.Value ?? "").Contains("Heading", StringComparison.OrdinalIgnoreCase)).Select(x => x.StyleRunProperties?.Color?.Val?.Value).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? heading;
            var section = template.MainDocumentPart?.Document.Body?.Elements<SectionProperties>().LastOrDefault();
            sourceMargin = section?.GetFirstChild<PageMargin>()?.CloneNode(true) as PageMargin; sourceSize = section?.GetFirstChild<PageSize>()?.CloneNode(true) as PageSize; columnCount = (uint)(section?.GetFirstChild<Columns>()?.ColumnCount?.Value ?? 1);
            titleAlignment = template.MainDocumentPart?.Document.Body?.Elements<Paragraph>().FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.InnerText))?.ParagraphProperties?.Justification?.Val?.Value;
        }
        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            foreach (var style in document.MainDocumentPart?.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
            {
                if (style.StyleRunProperties is null) continue;
                style.StyleRunProperties.RunFonts = new RunFonts { Ascii = font, HighAnsi = font };
                if (style.StyleId?.Value is "ResumeName" or "ResumeSection" or "ResumeRole") style.StyleRunProperties.Color = new Color { Val = heading };
            }
            var body = document.MainDocumentPart?.Document.Body; var finalSection = body?.Elements<SectionProperties>().LastOrDefault();
            if (finalSection is not null) { if (sourceMargin is not null) { finalSection.GetFirstChild<PageMargin>()?.Remove(); finalSection.Append(sourceMargin); } if (sourceSize is not null) { finalSection.GetFirstChild<PageSize>()?.Remove(); finalSection.PrependChild(sourceSize); } }
            var name = body?.Elements<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "ResumeName"); if (name is not null && titleAlignment is not null) { name.ParagraphProperties ??= new ParagraphProperties(); name.ParagraphProperties.Justification = new Justification { Val = titleAlignment }; }
            if (columnCount > 1) ApplyColumns(document, Math.Min(columnCount, 2));
            document.MainDocumentPart?.Document.Save();
        }
        return stream.ToArray();
    }

    private static void ApplyBuiltInLayout(WordprocessingDocument document, string templateId, string heading, string accent)
    {
        var body = document.MainDocumentPart?.Document.Body; if (body is null) return;
        var name = body.Elements<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "ResumeName");
        var subtitle = body.Elements<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "ResumeSubtitle");
        var contact = body.Elements<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "ResumeContact");
        if (templateId.Equals("classic", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var p in new[] { name, subtitle, contact }.Where(p => p is not null)) { p!.ParagraphProperties ??= new ParagraphProperties(); p.ParagraphProperties.Justification = new Justification { Val = JustificationValues.Left }; }
        }
        else if (templateId.Equals("modern", StringComparison.OrdinalIgnoreCase))
        {
            ApplyColumns(document, 2);
            if (name is not null) { name.ParagraphProperties ??= new ParagraphProperties(); name.ParagraphProperties.Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = "EAF4FF" }; }
        }
        else if (templateId.Equals("technical", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var p in body.Elements<Paragraph>().Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "ResumeSection"))
            { p.ParagraphProperties ??= new ParagraphProperties(); p.ParagraphProperties.Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = "F1F5F9" }; p.ParagraphProperties.Indentation = new Indentation { Left = "100" }; }
        }
    }

    private static void ApplyColumns(WordprocessingDocument document, uint count)
    {
        var ruleParagraph = document.MainDocumentPart?.Document.Descendants<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphBorders?.BottomBorder is not null);
        if (ruleParagraph is null) return; ruleParagraph.ParagraphProperties ??= new ParagraphProperties();
        ruleParagraph.ParagraphProperties.SectionProperties = new SectionProperties(new SectionType { Val = SectionMarkValues.Continuous }, new Columns { ColumnCount = (short)count, Space = "420", EqualWidth = true });
    }

    private static byte[] ApplyImageTheme(byte[] source, ImageTemplateAnalysis analysis)
    {
        var accentValue = analysis.AccentHex ?? "";
        var accent = Regex.IsMatch(accentValue, "^[0-9A-Fa-f]{6}$") ? accentValue.ToUpperInvariant() : Teal;
        if (!analysis.Layout.Equals("two-column", StringComparison.OrdinalIgnoreCase))
            return ApplyImageSingleColumnTheme(source, analysis with { AccentHex = accent });

        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var main = document.MainDocumentPart;
            var body = main?.Document.Body;
            if (main is null || body is null) return source;
            var paragraphs = body.Elements<Paragraph>().ToList();
            var name = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeName");
            var role = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeSubtitle");
            var contact = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeContact");
            var section = body.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true) as SectionProperties;
            var mainColumn = new List<Paragraph>();
            var sidebar = new List<Paragraph>();
            var destination = mainColumn;
            foreach (var paragraph in paragraphs)
            {
                var style = StyleOf(paragraph);
                if (style is "ResumeName" or "ResumeSubtitle" or "ResumeContact" || paragraph.ParagraphProperties?.ParagraphBorders is not null) continue;
                if (style == "ResumeSection")
                {
                    destination = IsImageSidebarSection(paragraph.InnerText) ? sidebar : mainColumn;
                    paragraph.ParagraphProperties ??= new ParagraphProperties();
                    paragraph.ParagraphProperties.ParagraphBorders = new ParagraphBorders(
                        new BottomBorder { Val = BorderValues.Single, Color = "DDD8E8", Size = 4, Space = 5 });
                }
                destination.Add((Paragraph)paragraph.CloneNode(true));
            }

            body.RemoveAllChildren();
            var leftIsSidebar = analysis.Sidebar.Equals("left", StringComparison.OrdinalIgnoreCase);
            var mainWidth = 7100;
            var sideWidth = 3200;
            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Nil }, new LeftBorder { Val = BorderValues.Nil },
                        new BottomBorder { Val = BorderValues.Nil }, new RightBorder { Val = BorderValues.Nil },
                        new InsideHorizontalBorder { Val = BorderValues.Nil }, new InsideVerticalBorder { Val = BorderValues.Nil })),
                new TableGrid(
                    new GridColumn { Width = (leftIsSidebar ? sideWidth : mainWidth).ToString() },
                    new GridColumn { Width = (leftIsSidebar ? mainWidth : sideWidth).ToString() }));

            var mainCell = ImageCell("FFFFFF", mainWidth, 300);
            var sideCell = ImageCell("F4F0FC", sideWidth, 260);
            mainCell.Append(StyledClone(name, "ResumeName", JustificationValues.Left));
            mainCell.Append(StyledClone(role, "ResumeSubtitle", JustificationValues.Left));
            foreach (var p in mainColumn) mainCell.Append(p);

            sideCell.Append(Paragraph("CONTACT", "ResumeSection"));
            if (contact is not null) sideCell.Append(StyledClone(contact, "ResumeContact", JustificationValues.Left));
            foreach (var p in sidebar) sideCell.Append(p);
            table.Append(leftIsSidebar ? new TableRow(sideCell, mainCell) : new TableRow(mainCell, sideCell));
            body.Append(table);

            section ??= new SectionProperties();
            section.RemoveAllChildren<PageSize>();
            section.RemoveAllChildren<PageMargin>();
            section.PrependChild(new PageMargin { Top = 480, Right = 480, Bottom = 480, Left = 480, Header = 180, Footer = 180 });
            section.PrependChild(new PageSize { Width = 12240, Height = 15840 });
            body.Append(section);

            var styles = main.StyleDefinitionsPart?.Styles;
            foreach (var style in styles?.Elements<Style>() ?? [])
            {
                var run = style.StyleRunProperties;
                if (run is null) continue;
                run.RunFonts = new RunFonts { Ascii = "Arial", HighAnsi = "Arial" };
                switch (style.StyleId?.Value)
                {
                    case "ResumeName":
                        run.Color = new Color { Val = accent }; run.FontSize = new FontSize { Val = "58" }; run.Bold = new Bold(); break;
                    case "ResumeSection":
                        run.Color = new Color { Val = accent }; run.FontSize = new FontSize { Val = "22" }; run.Bold = new Bold(); break;
                    case "ResumeSubtitle":
                        run.Color = new Color { Val = "666666" }; run.FontSize = new FontSize { Val = "30" }; break;
                    case "ResumeRole":
                        run.Color = new Color { Val = "5B5B5B" }; run.FontSize = new FontSize { Val = "20" }; run.Bold = new Bold(); break;
                    default:
                        run.Color = new Color { Val = "6B6B6B" }; run.FontSize = new FontSize { Val = "18" }; break;
                }
            }
            styles?.Save(); main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] ApplyImageSingleColumnTheme(byte[] source, ImageTemplateAnalysis analysis)
    {
        var accent = analysis.AccentHex;
        var serif = analysis.Typography.Equals("serif", StringComparison.OrdinalIgnoreCase);
        var font = serif ? "Times New Roman" : "Arial";
        var centeredHeader = analysis.HeaderAlignment.Equals("center", StringComparison.OrdinalIgnoreCase);
        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var styles = document.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            foreach (var style in styles?.Elements<Style>() ?? [])
            {
                var run = style.StyleRunProperties;
                if (run is null) continue;
                run.RunFonts = new RunFonts { Ascii = font, HighAnsi = font, EastAsia = font, ComplexScript = font };
                switch (style.StyleId?.Value)
                {
                    case "ResumeName":
                        run.Color = new Color { Val = accent };
                        run.FontSize = new FontSize { Val = serif ? "34" : "42" };
                        run.Bold = new Bold();
                        break;
                    case "ResumeSection":
                        run.Color = new Color { Val = accent };
                        run.FontSize = new FontSize { Val = serif ? "25" : "23" };
                        run.Bold = new Bold();
                        break;
                    case "ResumeRole":
                        run.Color = new Color { Val = "111111" };
                        run.FontSize = new FontSize { Val = serif ? "21" : "20" };
                        run.Bold = new Bold();
                        break;
                    case "ResumeSubtitle":
                    case "ResumeContact":
                        run.Color = new Color { Val = "111111" };
                        break;
                    default:
                        run.Color = new Color { Val = serif ? "111111" : "243B53" };
                        break;
                }
            }
            var body = document.MainDocumentPart?.Document.Body;
            foreach (var paragraph in body?.Elements<Paragraph>() ?? [])
            {
                var style = StyleOf(paragraph);
                paragraph.ParagraphProperties ??= new ParagraphProperties();
                if (centeredHeader && style is "ResumeName" or "ResumeSubtitle" or "ResumeContact")
                    paragraph.ParagraphProperties.Justification = new Justification { Val = JustificationValues.Center };
                if (style == "ResumeSection" && analysis.SectionRules)
                    paragraph.ParagraphProperties.ParagraphBorders = new ParagraphBorders(
                        new BottomBorder { Val = BorderValues.Single, Color = accent, Size = 7, Space = 2 });
                if (style == "ResumeRole")
                    RightAlignTrailingMetadata(paragraph);
            }
            foreach (var border in document.MainDocumentPart?.Document.Descendants<BottomBorder>() ?? [])
                border.Color = accent;
            foreach (var color in document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering?.Descendants<Color>() ?? [])
                color.Val = accent;
            var section = body?.Elements<SectionProperties>().LastOrDefault();
            if (section is not null && serif)
            {
                section.RemoveAllChildren<PageMargin>();
                section.Append(new PageMargin { Top = 620, Right = 720, Bottom = 620, Left = 720, Header = 180, Footer = 180 });
            }
            styles?.Save(); document.MainDocumentPart?.Document.Save();
        }
        return stream.ToArray();
    }

    private static void RightAlignTrailingMetadata(Paragraph paragraph)
    {
        var value = paragraph.InnerText.Trim();
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !DateRegex().IsMatch(parts[^1])) return;
        var left = string.Join(" | ", parts[..^1]);
        var right = parts[^1];
        var runProperties = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties;
        paragraph.RemoveAllChildren<Run>();
        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.Tabs = new Tabs(
            new TabStop { Val = TabStopValues.Right, Position = 10000 });
        var leftRun = new Run();
        if (runProperties is not null) leftRun.Append(runProperties.CloneNode(true));
        leftRun.Append(new Text(left) { Space = SpaceProcessingModeValues.Preserve }, new TabChar());
        var rightRun = new Run();
        if (runProperties is not null) rightRun.Append(runProperties.CloneNode(true));
        rightRun.Append(new Text(right) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(leftRun, rightRun);
    }

    private static bool IsImageSidebarSection(string heading)
    {
        var normalized = heading.Trim().TrimEnd(':');
        return normalized.Contains("SKILL", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("EDUCATION", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("CERTIF", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("LANGUAGE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("OTHER", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ADDITIONAL INFORMATION", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("CONTACT", StringComparison.OrdinalIgnoreCase);
    }

    private static TableCell ImageCell(string fill, int width, int padding) =>
        new(
            new TableCellProperties(
                new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Fill = fill },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top },
                new TableCellMargin(
                    new TopMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa },
                    new StartMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa },
                    new EndMargin { Width = padding.ToString(), Type = TableWidthUnitValues.Dxa })));

    private static byte[] ApplyPdfReferenceLayout(byte[] source)
    {
        using var stream = new MemoryStream(); stream.Write(source); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var main = document.MainDocumentPart;
            var body = main?.Document.Body;
            if (main is null || body is null) return source;

            var paragraphs = body.Elements<Paragraph>().ToList();
            var name = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeName");
            var role = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeSubtitle");
            var contact = paragraphs.FirstOrDefault(p => StyleOf(p) == "ResumeContact");
            var section = body.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true) as SectionProperties;

            var left = new List<Paragraph>();
            var right = new List<Paragraph>();
            var destination = left;
            foreach (var paragraph in paragraphs)
            {
                var style = StyleOf(paragraph);
                if (style is "ResumeName" or "ResumeSubtitle" or "ResumeContact" || paragraph.ParagraphProperties?.ParagraphBorders is not null) continue;
                if (style == "ResumeSection")
                {
                    var heading = paragraph.InnerText.Trim();
                    destination = IsSidebarSection(heading) ? left : right;
                }
                destination.Add((Paragraph)paragraph.CloneNode(true));
            }

            body.RemoveAllChildren();
            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Nil },
                        new LeftBorder { Val = BorderValues.Nil },
                        new BottomBorder { Val = BorderValues.Nil },
                        new RightBorder { Val = BorderValues.Nil },
                        new InsideHorizontalBorder { Val = BorderValues.Nil },
                        new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 18 })),
                new TableGrid(new GridColumn { Width = "5200" }, new GridColumn { Width = "5600" }));

            var headerLeft = Cell("E9E9E9", 5200);
            var headerRight = Cell("FFFFFF", 5600);
            headerLeft.Append(StyledClone(name, "ResumeName", JustificationValues.Left));
            if (contact is not null)
            {
                var contactHeading = Paragraph("CONTACT", "ResumeSection");
                contactHeading.ParagraphProperties!.SpacingBetweenLines = new SpacingBetweenLines { Before = "520", After = "180" };
                headerLeft.Append(contactHeading, StyledClone(contact, "ResumeContact", JustificationValues.Left));
            }
            var banner = StyledClone(role, "ResumeSubtitle", JustificationValues.Center);
            banner.ParagraphProperties ??= new ParagraphProperties();
            banner.ParagraphProperties.Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = "000000" };
            banner.ParagraphProperties.SpacingBetweenLines = new SpacingBetweenLines { Before = "220", After = "220" };
            foreach (var run in banner.Elements<Run>())
            {
                run.RunProperties ??= new RunProperties();
                run.RunProperties.Color = new Color { Val = "FFFFFF" };
                run.RunProperties.Bold = new Bold();
            }
            headerRight.Append(banner);
            table.Append(new TableRow(headerLeft, headerRight));

            var contentLeft = Cell("E9E9E9", 5200);
            var contentRight = Cell("FFFFFF", 5600);
            foreach (var p in left) contentLeft.Append(p);
            foreach (var p in right) contentRight.Append(p);
            table.Append(new TableRow(contentLeft, contentRight));
            body.Append(table);

            section ??= new SectionProperties();
            section.RemoveAllChildren<PageSize>();
            section.RemoveAllChildren<PageMargin>();
            section.PrependChild(new PageMargin { Top = 420, Right = 420, Bottom = 420, Left = 420, Header = 180, Footer = 180 });
            section.PrependChild(new PageSize { Width = 12240, Height = 15840 });
            body.Append(section);

            var styles = main.StyleDefinitionsPart?.Styles;
            foreach (var style in styles?.Elements<Style>() ?? [])
            {
                if (style.StyleRunProperties is null) continue;
                if (style.StyleId?.Value is "ResumeName" or "ResumeSection")
                {
                    style.StyleRunProperties.RunFonts = new RunFonts { Ascii = "Arial Narrow", HighAnsi = "Arial Narrow" };
                    style.StyleRunProperties.Color = new Color { Val = "000000" };
                    style.StyleRunProperties.Bold = new Bold();
                }
                else
                {
                    style.StyleRunProperties.RunFonts = new RunFonts { Ascii = "Arial", HighAnsi = "Arial" };
                }
            }
            styles?.Save();
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static string StyleOf(Paragraph paragraph) =>
        paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";

    private static HashSet<Paragraph> DetectHeaderParagraphs(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>().Where(p => !string.IsNullOrWhiteSpace(p.InnerText)).ToList();
        var firstSection = paragraphs.FindIndex(IsStructuralSectionHeading);
        var count = firstSection > 0 ? firstSection : Math.Min(3, paragraphs.Count);
        return paragraphs.Take(count).ToHashSet();
    }

    private static bool IsStructuralSectionHeading(Paragraph paragraph)
    {
        var value = paragraph.InnerText.Trim().TrimEnd(':');
        if (value.Length is < 3 or > 80) return false;
        if (KnownHeadings.Contains(value)) return true;
        var style = StyleOf(paragraph);
        var letters = value.Where(char.IsLetter).ToArray();
        var looksLikeSectionLabel = letters.Length > 1 && letters.All(char.IsUpper);
        return looksLikeSectionLabel &&
               (style.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
                style.Contains("Section", StringComparison.OrdinalIgnoreCase) ||
                paragraph.ParagraphProperties?.OutlineLevel is not null);
    }

    private static bool IsSemanticHeading(Paragraph paragraph)
    {
        if (IsStructuralSectionHeading(paragraph)) return true;
        var value = paragraph.InnerText.Trim();
        if (value.Length is < 2 or > 90) return false;
        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length > 1 && letters.All(char.IsUpper)) return true;
        var textLength = paragraph.Elements<Run>().Sum(run => run.InnerText.Length);
        var boldLength = paragraph.Elements<Run>()
            .Where(run => run.RunProperties?.Bold is not null || run.RunProperties?.BoldComplexScript is not null)
            .Sum(run => run.InnerText.Length);
        return textLength > 0 && boldLength >= textLength * 0.75;
    }

    private static bool IsSidebarSection(string heading) =>
        heading.Contains("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
        heading.Contains("PROFILE", StringComparison.OrdinalIgnoreCase) ||
        heading.Contains("SKILL", StringComparison.OrdinalIgnoreCase) ||
        heading.Contains("CONTACT", StringComparison.OrdinalIgnoreCase) ||
        heading.Contains("COMPETENC", StringComparison.OrdinalIgnoreCase) ||
        heading.Contains("LANGUAGE", StringComparison.OrdinalIgnoreCase);

    private static TableCell Cell(string fill, int width) =>
        new(
            new TableCellProperties(
                new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Fill = fill },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top },
                new TableCellMargin(
                    new TopMargin { Width = "220", Type = TableWidthUnitValues.Dxa },
                    new StartMargin { Width = "260", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "220", Type = TableWidthUnitValues.Dxa },
                    new EndMargin { Width = "260", Type = TableWidthUnitValues.Dxa })));

    private static Paragraph StyledClone(Paragraph? source, string style, JustificationValues alignment)
    {
        var paragraph = source is null ? Paragraph("", style) : (Paragraph)source.CloneNode(true);
        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.ParagraphStyleId = new ParagraphStyleId { Val = style };
        paragraph.ParagraphProperties.Justification = new Justification { Val = alignment };
        return paragraph;
    }

    private static void PopulatePlaceholders(WordprocessingDocument document, ParsedResume resume)
    {
        var lines = resume.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Clean).Where(x => x.Length > 0).ToList();
        var header = ExtractHeader(resume.Text, lines);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{{NAME}}"] = header.Name,
            ["{{ROLE}}"] = header.Role,
            ["{{CONTACT}}"] = string.Join(" | ", header.ContactParts),
            ["{{EMAIL}}"] = EmailRegex().Match(resume.Text).Value,
            ["{{PHONE}}"] = PhoneRegex().Match(resume.Text).Value,
            ["{{LINKEDIN}}"] = LinkedInRegex().Match(resume.Text).Value,
            ["{{RESUME_CONTENT}}"] = string.Join(Environment.NewLine, lines.Where(x => !header.SourceLines.Contains(x)))
        };
        foreach (var paragraph in document.MainDocumentPart?.Document.Descendants<Paragraph>() ?? [])
        {
            var combined = paragraph.InnerText; if (!values.Keys.Any(key => combined.Contains(key, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var value in values) combined = combined.Replace(value.Key, value.Value, StringComparison.OrdinalIgnoreCase);
            var firstRun = paragraph.Elements<Run>().FirstOrDefault(); var runProperties = firstRun?.RunProperties?.CloneNode(true) as RunProperties;
            paragraph.RemoveAllChildren<Run>(); var run = new Run(); if (runProperties is not null) run.Append(runProperties);
            var segments = combined.Replace("\r", "").Split('\n');
            for (var i = 0; i < segments.Length; i++) { if (i > 0) run.Append(new Break()); run.Append(new Text(segments[i]) { Space = SpaceProcessingModeValues.Preserve }); }
            paragraph.Append(run);
        }
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            Style("ResumeBody", "Resume Body", 21, "243B53", false, 0, 80, 260),
            Style("ResumeName", "Resume Name", 34, Navy, true, 0, 30, 240),
            Style("ResumeSubtitle", "Resume Subtitle", 23, Teal, true, 0, 45, 240),
            Style("ResumeContact", "Resume Contact", 18, Gray, false, 0, 70, 230),
            Style("ResumeSection", "Resume Section", 23, Navy, true, 170, 70, 240, true),
            Style("ResumeRole", "Resume Role", 21, Navy, true, 90, 35, 240, keepNext: true),
            Style("ResumeBullet", "Resume Bullet", 20, "243B53", false, 0, 55, 250));
        part.Styles.Save();
    }

    private static Style Style(string id, string name, int halfPoints, string color, bool bold, int before, int after, int line, bool caps = false, bool keepNext = false)
    {
        var run = new StyleRunProperties(new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" }, new FontSize { Val = halfPoints.ToString() }, new Color { Val = color });
        if (bold) run.Append(new Bold()); if (caps) run.Append(new Caps());
        var paragraph = new StyleParagraphProperties(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString(), Line = line.ToString(), LineRule = LineSpacingRuleValues.Auto });
        if (keepNext) paragraph.Append(new KeepNext());
        return new Style(new StyleName { Val = name }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "ResumeBody" }, paragraph, run) { Type = StyleValues.Paragraph, StyleId = id, CustomStyle = true };
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        var level = new Level(new NumberingFormat { Val = NumberFormatValues.Bullet }, new LevelText { Val = "•" }, new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation { Left = "420", Hanging = "220" }), new NumberingSymbolRunProperties(new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" }, new Color { Val = Teal }))
        { LevelIndex = 0 };
        part.Numbering = new Numbering(new AbstractNum(level) { AbstractNumberId = 1 }, new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        part.Numbering.Save();
    }

    private static Paragraph Paragraph(string text, string style, JustificationValues? alignment = null, bool numbering = false)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (alignment is not null) properties.Append(new Justification { Val = alignment });
        if (numbering) properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }));
        return new Paragraph(properties, new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Paragraph LabelParagraph(string label, string detail)
    {
        var paragraph = Paragraph("", "ResumeBody"); paragraph.RemoveAllChildren<Run>();
        paragraph.Append(new Run(new RunProperties(new Bold(), new Color { Val = Navy }), new Text(label + ": ")), new Run(new Text(detail)));
        return paragraph;
    }

    private static Paragraph AccentRule()
    {
        var borders = new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Color = Teal, Size = 14, Space = 6 });
        return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "80" }, borders), new Run(new Text(" ")));
    }

    private static HeaderInfo ExtractHeader(string text, IReadOnlyList<string> lines)
    {
        var email = EmailRegex().Match(text).Value;
        var phoneRaw = PhoneRegex().Matches(text).Select(m => m.Value).FirstOrDefault(value => value.Count(char.IsDigit) is >= 10 and <= 12) ?? "";
        var phoneDigits = new string(phoneRaw.Where(char.IsDigit).ToArray());
        var phone = phoneDigits.Length == 10 ? $"({phoneDigits[..3]}) {phoneDigits[3..6]}-{phoneDigits[6..]}" : phoneRaw.Trim();
        var linkedIn = LinkedInRegex().Match(text).Value;
        var contactLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(email) && line.Contains(email, StringComparison.OrdinalIgnoreCase));
        var first = contactLine ?? lines.FirstOrDefault(line => !line.Contains("contact details", StringComparison.OrdinalIgnoreCase)) ?? "Candidate Name";
        var name = first;
        if (!string.IsNullOrWhiteSpace(email) && first.Contains(email, StringComparison.OrdinalIgnoreCase)) name = first[..first.IndexOf(email, StringComparison.OrdinalIgnoreCase)];
        name = LeadingArtifactRegex().Replace(name, "").Replace("Contact details", "", StringComparison.OrdinalIgnoreCase).Trim(' ', '-', '|');
        string? detectedNameLine = null;
        if (name.Length is < 2 or > 60)
        {
            detectedNameLine = lines.FirstOrDefault(IsLikelyCandidateName);
            name = detectedNameLine ?? "Candidate Name";
        }
        var role = "";
        if (contactLine is not null)
        {
            role = contactLine[(contactLine.IndexOf(email, StringComparison.OrdinalIgnoreCase) + email.Length)..];
            role = PhoneRegex().Replace(role, " "); role = LinkedInRegex().Replace(role, " ");
            role = Regex.Replace(role, @"\b(?:Mobile|WhatsApp|Phone|LinkedIn|Contact details)\b[:/]*", " ", RegexOptions.IgnoreCase);
            role = WhitespaceRegex().Replace(role, " ").Trim(' ', '-', '|', '(', ')');
        }
        if (role.Length is < 3 or > 70) role = lines.Skip(1).FirstOrDefault(x => x.Length is > 3 and < 70 && !x.Contains(',') && !EmailRegex().IsMatch(x) && !PhoneRegex().IsMatch(x) && !IsHeading(x)) ?? "";
        var contacts = new[] { email, phone, linkedIn }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var sources = lines.Where(x => x.Contains("contact details", StringComparison.OrdinalIgnoreCase))
            .Concat(contactLine is null ? [] : [contactLine])
            .Concat(detectedNameLine is null ? [] : [detectedNameLine])
            .Concat(lines.Where(x => x == role && role.Length > 0))
            .ToHashSet();
        return new HeaderInfo(name, role, contacts, sources);
    }

    private static bool IsLikelyCandidateName(string value)
    {
        var text = value.Trim();
        if (text.Length is < 4 or > 60 || text.Contains(',') || text.Contains('|') ||
            EmailRegex().IsMatch(text) || PhoneRegex().IsMatch(text) || LinkedInRegex().IsMatch(text) ||
            IsHeading(text))
            return false;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 6) return false;
        return words.All(word =>
        {
            var letters = word.Where(char.IsLetter).ToArray();
            return letters.Length > 0 && char.IsUpper(letters[0]) && letters.Skip(1).Any(char.IsLower);
        });
    }

    private static bool IsHeading(string text) => KnownHeadings.Contains(text) || (text.Length is >= 3 and <= 32 && text.Any(char.IsLetter) && text.All(c => !char.IsLetter(c) || char.IsUpper(c)));
    private static bool TrySplitHeading(string text, out string heading, out string content)
    {
        foreach (var known in KnownHeadings.OrderByDescending(x => x.Length))
            if (text.StartsWith(known + " ", StringComparison.OrdinalIgnoreCase)) { heading = known; content = text[(known.Length + 1)..].Trim(); return true; }
        heading = content = ""; return false;
    }
    private static bool IsRoleLine(string text) => text.StartsWith('#') || (text.Contains('|') && DateRegex().IsMatch(text));
    private static bool ShouldBullet(string text) => text.Length > 55 || text.StartsWith('-') || text.StartsWith('•');
    private static bool IsLabelLine(string text, out string label, out string detail) { var index = text.IndexOf(':'); if (index is > 1 and < 32) { label = text[..index].Trim(); detail = text[(index + 1)..].Trim(); return detail.Length > 0; } label = detail = ""; return false; }

    private static string Clean(string value)
    {
        var text = WhitespaceRegex().Replace(value, " ").Trim(); foreach (var correction in Corrections) text = Regex.Replace(text, $@"\b{Regex.Escape(correction.Key)}\b", correction.Value, RegexOptions.IgnoreCase);
        return LeadingPronounRegex().Replace(text, "").Trim(' ', '-', '•');
    }

    private sealed record HeaderInfo(string Name, string Role, List<string> ContactParts, HashSet<string> SourceLines);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingPronounPattern = new(@"^(?:I|My|We|Our)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"\b[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"(?:\+?\d[\d\s().-]{7,}\d)", RegexOptions.Compiled);
    private static readonly Regex LinkedInPattern = new(@"(?:https?://)?(?:www\.)?linkedin\.com/in/[\w-]+/?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DatePattern = new(@"\b(?:19|20)\d{2}\b|\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingArtifactPattern = new(@"^\d{8,}", RegexOptions.Compiled);
    private static readonly Regex GeneratedTailoringPattern = new(
        @"(?:(?:Demonstrated strengths include|Relevant strengths include|Relevant technologies:|Relevant capabilities:)\s+[^.\r\n]{1,300}\.|Applied\s+[^.\r\n]{1,300}\s+in relevant (?:technical delivery|professional work)\.)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static Regex WhitespaceRegex() => WhitespacePattern;
    private static Regex LeadingPronounRegex() => LeadingPronounPattern;
    private static Regex EmailRegex() => EmailPattern;
    private static Regex PhoneRegex() => PhonePattern;
    private static Regex LinkedInRegex() => LinkedInPattern;
    private static Regex DateRegex() => DatePattern;
    private static Regex LeadingArtifactRegex() => LeadingArtifactPattern;
}