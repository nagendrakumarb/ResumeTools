using System.Text.RegularExpressions;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed partial class SimilarityService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "in", "is", "it",
        "of", "on", "or", "our", "that", "the", "their", "this", "to", "was", "we", "will", "with", "you", "your",
        "about", "across", "applicant", "applicants", "apply", "base", "benefit", "candidate", "candidates", "company",
        "deliver", "employment", "equal", "including", "job", "notice", "opportunity", "own", "please", "position",
        "provide", "required", "requirement", "requirements", "responsibilities", "role", "team", "work", "working",
        "ability", "accommodation", "age", "citizenship", "disability", "diversity", "equity", "gender", "hundred",
        "intense", "legally", "origin", "protected", "reasonable", "status", "veteran",
        "bachelor", "bachelors", "degree", "desired", "equivalent", "implementation", "majorly", "minimum",
        "typically", "education", "experience", "years", "applications", "development", "release", "testing"
    };

    private static readonly string[] RequirementVocabulary =
    [
        ".NET", "ASP.NET", ".NET Core", "C#", "F#", "VB.NET", "Java", "Kotlin", "Go", "Golang", "Python",
        "JavaScript", "TypeScript", "Node.js", "React", "Angular", "Vue", "Blazor", "MAUI",
        "SQL", "SQL Server", "PostgreSQL", "MySQL", "Oracle", "MongoDB", "Cosmos DB", "Redis", "Elasticsearch",
        "Azure", "AWS", "GCP", "Azure Functions", "Lambda", "App Service", "Key Vault", "Service Bus",
        "Docker", "Kubernetes", "Terraform", "Ansible", "Jenkins", "GitHub Actions", "Azure DevOps", "CI/CD", "Git",
        "REST", "RESTful API", "Web API", "GraphQL", "gRPC", "Microservices", "Event Driven Architecture",
        "Distributed Systems", "System Design", "Software Architecture", "Solution Architecture", "API Design",
        "Authentication", "Authorization", "Identity", "DynamoDB", "SDK", "Developer Tools", "Design Docs",
        "Entity Framework", "Dapper", "Spring Boot", "OAuth", "OAuth 2.0", "JWT", "SAML",
        "Unit Testing", "Integration Testing", "Test Automation", "TDD", "BDD", "Observability", "Monitoring",
        "Application Insights", "OpenTelemetry", "Incident Response", "On-call", "Remediation", "Site Reliability", "DevOps", "DevSecOps",
        "Machine Learning", "Artificial Intelligence", "Data Engineering", "Data Science", "ETL",
        "Agile", "Scrum", "Technical Leadership", "Mentoring", "Pairing", "Code Quality", "Stakeholder Management", "Communication",
        "Problem Solving", "Code Review", "Performance Optimization", "Security", "Cloud", "Backend", "Frontend",
        "Full Stack", "Scalability", "Reliability", "High Availability"
    ];

    public MatchResult Compare(string resume, string jobDescription)
    {
        var jobTerms = ExtractRequirements(JobTextSanitizer.RelevantDescription(jobDescription));
        if (jobTerms.Count == 0)
            throw new InvalidOperationException("The job description does not contain enough recognizable technical or professional requirements.");

        var matched = jobTerms.Where(term => ContainsRequirement(resume, term)).ToArray();
        var missing = jobTerms.Where(term => !ContainsRequirement(resume, term)).ToArray();
        var termCoverage = matched.Length / (double)jobTerms.Count;
        var requiredContextLines = Math.Min(4, Math.Max(1, (int)Math.Ceiling(matched.Length / 5d)));
        var contextLines = CountContextLines(resume, matched);
        var distributionCoverage = Math.Min(1d, contextLines / (double)requiredContextLines);
        var score = Math.Round(termCoverage * (0.85d + 0.15d * distributionCoverage) * 100d, 1);
        var summary = score >= 95
            ? "Excellent contextual requirement coverage. Verify that every listed skill is truthful and supported by your experience."
            : score >= 75
                ? missing.Length == 0
                    ? "All identified terms are present, but too many are concentrated in too few resume lines. Distribute them across truthful summary, skills, and experience evidence."
                    : "Strong requirement coverage. Address the remaining relevant gaps before applying."
                : score >= 50
                    ? "Partial requirement coverage. Tailor the skills and achievement evidence to this role."
                    : "Low requirement coverage. Add only truthful job requirements and supporting experience before applying.";
        return new MatchResult(score, matched.Take(20).ToArray(), missing.Take(20).ToArray(), summary);
    }

    private static int CountContextLines(string resume, IReadOnlyCollection<string> matchedTerms)
    {
        var lines = resume.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Count(line => matchedTerms.Any(term => ContainsRequirement(line, term)));
    }

    private static List<string> ExtractRequirements(string text)
    {
        var recognized = RequirementVocabulary
            .Where(term => ContainsRequirement(text, term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(term => term.Count(character => character == ' ') + 1)
            .ThenBy(term => term)
            .Take(20)
            .ToList();
        if (recognized.Count >= 5) return recognized;

        var fallback = WordRegex().Matches(text)
            .Select(match => match.Value.Trim())
            .Where(term => term.Length >= 3 &&
                           !term.All(char.IsDigit) &&
                           !StopWords.Contains(term))
            .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key.Length)
            .Select(group => ToDisplayTerm(group.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20 - recognized.Count);
        recognized.AddRange(fallback);
        return recognized.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    private static bool ContainsRequirement(string text, string term)
    {
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ToDisplayTerm(string value) =>
        value.Length <= 4 && value.All(character => !char.IsLetter(character) || char.IsUpper(character))
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    [GeneratedRegex(@"[\p{L}\p{N}+#.]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
