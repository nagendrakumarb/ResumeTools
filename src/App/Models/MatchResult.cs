namespace ProfessionalHub.ResumeTools.Models;

public sealed record MatchResult(double Score, IReadOnlyList<string> MatchedTerms, IReadOnlyList<string> MissingTerms, string Summary);
public sealed record AnalysisRecord(DateTimeOffset CreatedAt, double Score, IReadOnlyList<string> MatchedTerms, IReadOnlyList<string> MissingTerms);
