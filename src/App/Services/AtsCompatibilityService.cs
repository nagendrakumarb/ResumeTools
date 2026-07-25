using System.Text.RegularExpressions;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed partial class AtsCompatibilityService
{
    private static readonly string[] ActionVerbs = ["achieved", "added", "adjusted", "automated", "built", "created", "delivered", "designed", "developed", "drove", "implemented", "improved", "increased", "integrated", "launched", "led", "managed", "optimized", "reduced", "reviewed", "saved", "scaled", "stored", "streamlined", "supported", "used", "worked", "wrote"];
    private static readonly string[] HardSkills = [".net", "asp.net", "azure", "aws", "c#", "java", "javascript", "python", "sql", "docker", "kubernetes", "git", "devops", "api", "microservices", "mongodb", "react", "angular", "power bi"];
    private static readonly string[] SoftSkills = ["communication", "leadership", "collaboration", "analytical", "problem solving", "adaptable", "adaptability", "organized", "creative", "mentoring", "teamwork", "strategic", "project delivery"];
    private static readonly string[] Pronouns = ["i", "me", "my", "mine", "we", "our", "ours"];
    private static readonly string[] CommonMisspellings = ["recieve", "seperate", "occured", "managment", "experiance", "responsibile", "developement", "acheived", "sucessful", "teh", "adn", "wich"];

    public AtsResult Analyze(ParsedResume resume)
    {
        var text = resume.Text;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Choose a resume before running the ATS scan.");

        var lower = text.ToLowerInvariant();
        var wordCount = WordRegex().Matches(text).Count;
        var emailCount = EmailRegex().Matches(text).Count;
        var phoneCount = PhoneRegex().Matches(text)
            .Select(match => match.Value)
            .Where(IsPlausiblePhone)
            .Select(value => new string(value.Where(char.IsDigit).ToArray()))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var metricCount = MetricRegex().Matches(text).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var verbsFound = ActionVerbs.Where(lower.Contains).Distinct().ToArray();
        var tokens = WordRegex().Matches(lower).Select(x => x.Value).ToArray();
        var personalPronouns = tokens.Count(Pronouns.Contains);
        var hardSkills = HardSkills.Where(lower.Contains).ToArray();
        var softSkills = SoftSkills.Where(lower.Contains).ToArray();
        var misspellings = CommonMisspellings.Where(lower.Contains).ToArray();
        var repeated = tokens.Where(x => x.Length > 3).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault();
        var linkedin = lower.Contains("linkedin.com/in/");
        var dateCount = DateRegex().Matches(text).Count;
        var uniqueRatio = tokens.Length == 0 ? 0 : tokens.Distinct().Count() * 100d / tokens.Length;
        var sentenceCount = Math.Max(1, SentenceUnitRegex().Split(text).Count(x => WordRegex().Matches(x).Count >= 3));
        var averageSentence = tokens.Length / (double)sentenceCount;

        var checks = new List<AtsCheck>
        {
            Simple("Document", "File type", 100, $"{resume.FileType} document", "A standard resume file type was detected.", "Use PDF or DOCX.", 0.5),
            Simple("Document", "File size", resume.FileSizeBytes <= 5 * 1024 * 1024 ? 100 : 50, $"{resume.FileSizeBytes / 1024d:N0} KB; target below 5 MB", resume.FileSizeBytes <= 5 * 1024 * 1024 ? "The file should upload reliably to most ATS portals." : "The file may exceed some portal limits.", "Compress images and remove embedded media.", 0.5),
            PageCheck(resume.PageCount),
            FontCheck(resume.AverageFontSize),
            BoldCheck(resume.BoldPercentage),
            ContactCheck("Email address", emailCount, "professional email address", 0.75),
            ContactCheck("Phone number", phoneCount, "reachable phone number with country or area code", 0.75),
            Simple("Identity", "LinkedIn URL", linkedin ? 100 : 0, linkedin ? "LinkedIn profile URL detected" : "No LinkedIn profile URL detected", linkedin ? "The professional profile is accessible to recruiters." : "No professional profile link was identified.", "Add a concise linkedin.com/in/... URL if the profile is current.", 0.5),
            SectionCheck("Professional summary", lower, ["summary", "profile", "objective"], "summary/profile heading", "Add a concise 3–5 line professional summary using role-relevant strengths.", 1.0),
            SectionCheck("Work experience", lower, ["experience", "employment", "work history"], "experience heading", "Add a clearly labelled Experience section with employer, role, dates, and achievements.", 1.5),
            SectionCheck("Skills section", lower, ["skills", "technical skills", "core competencies"], "skills heading", "Add a plainly labelled Skills section containing only skills you can demonstrate.", 1.25),
            SectionCheck("Education section", lower, ["education", "academic", "qualification"], "education heading", "Add a clearly labelled Education section with qualification and institution.", 0.75),
            Simple("Structure", "Date formatting", dateCount >= 2 ? 100 : dateCount == 1 ? 60 : 0, $"{dateCount} conventionally formatted date expressions detected", dateCount >= 2 ? "Employment or education dates appear machine-readable." : "Date coverage or formatting may be inconsistent.", "Use consistent formats such as Jan 2022 - Mar 2024.", 0.75),
            LengthCheck(wordCount),
            MetricCheck(metricCount),
            ActionVerbCheck(verbsFound),
            ExtractionCheck(wordCount, text),
            Simple("Language", "Personal pronouns", personalPronouns == 0 ? 100 : Math.Max(0, 100 - personalPronouns * 20), $"{personalPronouns} first-person pronouns detected", personalPronouns == 0 ? "Resume language is concise and conventionally impersonal." : "Personal pronouns weaken concise resume style.", "Remove I, me, my, we, and our where sentences remain clear.", 0.75),
            Simple("Language", "Spelling signals", misspellings.Length == 0 ? 100 : Math.Max(0, 100 - misspellings.Length * 20), misspellings.Length == 0 ? "No common misspellings detected" : $"Possible misspellings: {string.Join(", ", misspellings)}", misspellings.Length == 0 ? "No obvious spelling issues were found by the offline dictionary." : "Potential spelling errors reduce polish and keyword matching.", "Proofread with a full grammar tool; the offline check covers common errors only.", 1),
            Simple("Language", "Repetition", RepetitionScore(repeated, tokens.Length), repeated is null ? "No repeated content detected" : $"Most repeated content word: '{repeated.Key}' ({repeated.Count()} times)", RepetitionScore(repeated, tokens.Length) >= 99.5 ? "Word repetition is proportionate to the document length." : "One term may be overused and reduce readability.", "Replace unnecessary repetition with specific evidence or precise alternatives.", 0.75),
            Simple("Language", "Vocabulary variety", Math.Clamp(uniqueRatio * 2.5, 0, 100), $"{uniqueRatio:0}% unique-word ratio", uniqueRatio >= 35 ? "Vocabulary is varied without appearing excessively repetitive." : "Vocabulary variety is limited.", "Use precise role-specific nouns and varied action verbs without adding jargon.", 0.75),
            Simple("Language", "Reading clarity", averageSentence is >= 8 and <= 24 ? 100 : averageSentence <= 32 ? 70 : 40, $"Average sentence length: {averageSentence:0.0} words", averageSentence <= 24 ? "Sentence length is generally easy to scan." : "Long sentences may reduce recruiter and ATS readability.", "Keep bullets concise, ideally one achievement per line.", 1),
            SkillsCheck("Hard skills", hardSkills, 8, 1.25),
            SkillsCheck("Soft skills", softSkills, 4, 0.5),
            SkillsRatioCheck(hardSkills.Length, softSkills.Length)
        };

        var score = Math.Round(checks.Sum(x => x.Score * x.Weight) / checks.Sum(x => x.Weight));
        var incomplete = checks.Count(x => !x.IsComplete);
        var summary = score >= 90
            ? $"Excellent ATS foundation at {score:0}%. {incomplete} area{(incomplete == 1 ? "" : "s")} can still be strengthened for a more complete result."
            : score >= 75
                ? $"Good ATS compatibility at {score:0}%. Improve the lowest-scoring categories before submitting the resume."
                : score >= 60
                    ? $"Moderate ATS compatibility at {score:0}%. Several important signals are only partially covered."
                    : $"Low ATS compatibility at {score:0}%. Prioritize section structure, readable content, and evidence-based achievements.";
        return new AtsResult(score, Grade(score), checks, new SkillSummary(hardSkills, softSkills), summary);
    }

    private static string Grade(double score) => score switch { >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 60 => "D", _ => "F" };

    private static AtsCheck ContactCheck(string name, int count, string item, double weight)
    {
        var score = count == 1 ? 100 : count > 1 ? 75 : 0;
        var evidence = count == 0 ? $"No {item} detected" : $"{count} {item}{(count == 1 ? "" : "s")} detected";
        var assessment = count == 1 ? $"Complete: one {item} is available to recruiters." : count > 1 ? $"Mostly covered, but multiple entries can confuse contact extraction." : "Missing: an ATS cannot reliably provide recruiter contact information.";
        var improvement = count == 1 ? "No improvement required." : count > 1 ? $"Keep one primary {item}." : $"Add one {item} in the main document body, not only in a header, footer, or image.";
        return new AtsCheck("Identity", name, score, evidence, assessment, improvement, weight);
    }

    private static bool IsPlausiblePhone(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length is < 10 or > 12) return false;
        if (Regex.Matches(value, @"\b(?:19|20)\d{2}\b").Count >= 2) return false;
        return value.Contains('(') || value.TrimStart().StartsWith('+') || !value.Contains(" - ");
    }

    private static double RepetitionScore(IGrouping<string, string>? repeated, int tokenCount)
    {
        if (repeated is null || tokenCount == 0) return 100;
        var count = repeated.Count();
        var share = count * 100d / tokenCount;
        if (count <= 10 || share <= 1.5) return 100;
        return Math.Max(20, 100 - (count - 10) * 8);
    }

    private static AtsCheck SectionCheck(string name, string text, string[] headings, string label, string improvement, double weight)
    {
        var matches = headings.Where(text.Contains).ToArray();
        var score = matches.Length > 0 ? 100 : 0;
        return new AtsCheck("Structure", name, score, matches.Length > 0 ? $"Detected: {string.Join(", ", matches)}" : $"No recognized {label} detected",
            score == 100 ? "Fully covered: a conventional heading should be recognizable by most ATS parsers." : "Not covered: the content may exist, but a standard section heading was not recognized.",
            score == 100 ? "No structural improvement required; keep the heading simple." : improvement, weight);
    }

    private static AtsCheck LengthCheck(int words)
    {
        double score = words switch { < 100 => 15, < 250 => 55 + (words - 100) * 45d / 150, <= 1200 => 100, <= 1500 => 100 - (words - 1200) * 25d / 300, <= 2000 => 75 - (words - 1500) * 45d / 500, _ => 25 };
        var assessment = score >= 99.5 ? "Fully covered: the resume is within the broadly readable ATS range." : words < 250 ? "The resume may be too brief to provide enough searchable evidence." : "The resume is long enough that important evidence may be diluted.";
        var improvement = score >= 99.5 ? "No length improvement required; relevance and clarity still matter." : words < 250 ? "Add concise, evidence-based experience and skills; target at least 250 words." : "Remove repetition and older low-value detail; aim for roughly 400–1,200 words.";
        return new AtsCheck("Document", "Word count", Math.Round(Math.Clamp(score, 0, 100)), $"{words:N0} words detected; preferred range: 250–1,200", assessment, improvement, 1);
    }

    private static AtsCheck MetricCheck(int count)
    {
        var score = Math.Min(100, count / 6d * 100);
        var assessment = count >= 6 ? "Fully covered: the resume repeatedly demonstrates measurable impact." : count >= 3 ? "Partially covered: some achievements are quantified, but evidence is inconsistent." : count > 0 ? "Lightly covered: very few achievements contain measurable outcomes." : "Not covered: no clear numerical impact was detected.";
        var improvement = count >= 6 ? "Maintain the quality of the metrics and ensure each is truthful and contextual." : $"Add {Math.Max(0, 6 - count)} more quantified achievement{(6 - count == 1 ? "" : "s")} using percentages, money, scale, time, or volume.";
        return new AtsCheck("Impact", "Measurable achievements", Math.Round(score), $"{count} distinct numeric result{(count == 1 ? "" : "s")} detected; target: 6+", assessment, improvement, 1.5);
    }

    private static AtsCheck ActionVerbCheck(IReadOnlyCollection<string> verbs)
    {
        var score = Math.Min(100, verbs.Count / 8d * 100);
        var assessment = verbs.Count >= 8 ? "Fully covered: achievement language is varied and action-oriented." : verbs.Count >= 4 ? "Partially covered: action language exists but needs more variety." : "Weak coverage: bullets may read as responsibilities instead of achievements.";
        var improvement = verbs.Count >= 8 ? "No improvement required; avoid repeating the same opening verb." : $"Use {8 - verbs.Count} or more additional truthful action verbs at the start of achievement bullets.";
        return new AtsCheck("Impact", "Action-oriented language", Math.Round(score), $"{verbs.Count} distinct action verbs detected" + (verbs.Count > 0 ? $": {string.Join(", ", verbs.Take(8))}" : ""), assessment, improvement, 1.25);
    }

    private static AtsCheck ExtractionCheck(int words, string text)
    {
        var replacementCharacters = text.Count(c => c == '\uFFFD');
        var score = wordCountScore(words) - Math.Min(50, replacementCharacters * 5);
        var assessment = score >= 95 ? "The document produced a strong, clean text extraction for ATS processing." : score >= 70 ? "Most text was extracted, but formatting or encoding may reduce parser accuracy." : "Text extraction is weak and the resume may rely on scans, graphics, text boxes, or complex columns.";
        var improvement = score >= 95 ? "No extraction improvement required; retain a simple single- or two-column structure." : "Use selectable text, standard fonts, simple headings, and minimal tables, text boxes, icons, and graphics.";
        return new AtsCheck("Document", "ATS text extraction", Math.Round(Math.Clamp(score, 0, 100)), $"{words:N0} readable words; {replacementCharacters} invalid character{(replacementCharacters == 1 ? "" : "s")}", assessment, improvement, 1.5);
        static double wordCountScore(int count) => count >= 250 ? 100 : count >= 100 ? 60 + (count - 100) * 40d / 150 : count;
    }

    private static AtsCheck Simple(string group, string name, double score, string evidence, string assessment, string improvement, double weight) =>
        new(group, name, Math.Round(score), evidence, assessment, score >= 99.5 ? "No improvement required." : improvement, weight);

    private static AtsCheck PageCheck(int pages) => Simple("Document", "Page count", pages is 1 or 2 ? 100 : pages == 3 ? 70 : 35,
        $"{pages} page{(pages == 1 ? "" : "s")} detected; preferred: 1-2", pages <= 2 ? "Page count is concise for most applications." : "The resume is longer than most recruiters expect.", "Shorten older or less relevant content; target one or two pages.", 1);

    private static AtsCheck FontCheck(double size) => Simple("Formatting", "Font size", size == 0 ? 60 : size is >= 10 and <= 12.5 ? 100 : size is >= 9 and <= 14 ? 70 : 35,
        size == 0 ? "Font size metadata unavailable" : $"Average body glyph size: {size:0.0} pt; preferred: 10-12.5 pt", size is >= 10 and <= 12.5 ? "Font sizing is conventionally readable." : "Font sizing may be too small or large for comfortable scanning.", "Use approximately 10-12 pt body text and larger section headings.", 0.75);

    private static AtsCheck BoldCheck(double percent) => Simple("Formatting", "Bold usage", percent is >= 2 and <= 18 ? 100 : percent <= 30 ? 70 : 40,
        $"{percent:0.0}% of detected text is bold; preferred: 2-18%", percent is >= 2 and <= 18 ? "Bold emphasis appears balanced." : "Bold usage may be too limited or excessive.", "Reserve bold for name, role, headings, employers, and key labels.", 0.5);

    private static AtsCheck SkillsCheck(string name, string[] skills, int target, double weight) => Simple("Skills", name, Math.Min(100, skills.Length * 100d / target),
        $"{skills.Length} detected: {(skills.Length == 0 ? "none" : string.Join(", ", skills))}", skills.Length >= target ? "Skill coverage is broad." : "Skill coverage is present but could be clearer.", $"Add relevant, demonstrable skills; target at least {target} explicit items.", weight);

    private static AtsCheck SkillsRatioCheck(int hard, int soft)
    {
        var ratio = soft == 0 ? hard : hard / (double)soft;
        return Simple("Skills", "Skills efficiency ratio", ratio is >= 1.5 and <= 5 ? 100 : 65, $"{hard} hard skills / {soft} soft skills; ratio {ratio:0.00}",
            ratio is >= 1.5 and <= 5 ? "The balance appropriately emphasizes technical capability." : "The hard-to-soft skill balance may need adjustment.", "Prioritize role-specific hard skills and support them with a smaller set of credible soft skills.", 0.5);
    }

    [GeneratedRegex(@"\b[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}\b")] private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?:\+?\d[\d\s().-]{7,}\d)")] private static partial Regex PhoneRegex();
    [GeneratedRegex(@"(?:\b\d+(?:\.\d+)?%|[$₹€£]\s?\d[\d,.]*|\b\d{2,}(?:[,.]\d+)*\+?\b)")] private static partial Regex MetricRegex();
    [GeneratedRegex(@"[\p{L}\p{N}+#.-]+")] private static partial Regex WordRegex();
    [GeneratedRegex(@"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+\d{4}|\b\d{1,2}[/-]\d{4}|\b\d{4}\s*[-–]\s*(?:\d{4}|Present|Current)", RegexOptions.IgnoreCase)] private static partial Regex DateRegex();
    [GeneratedRegex(@"[.!?]+(?:\s|$)")] private static partial Regex SentenceRegex();
    [GeneratedRegex(@"(?:[.!?]+|\r?\n+)\s*")] private static partial Regex SentenceUnitRegex();
}
