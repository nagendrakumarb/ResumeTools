namespace ProfessionalHub.ResumeTools.Models;

public sealed record ResumeFixOutcome(string Name, string Status, string Detail);

public enum ResumeGenerationStatus
{
    Verified,
    ReviewRecommended,
    ManualCorrectionRequired,
    GenerationFailed
}

public enum ResumeSectionKind
{
    Contact,
    ProfessionalSummary,
    Skills,
    WorkExperience,
    Projects,
    Education,
    Certifications,
    Achievements,
    Other
}

public sealed record ResumeFact(
    string Id,
    string Value,
    ResumeSectionKind Section,
    string Kind);

public sealed record ResumeSectionInventory(
    ResumeSectionKind Kind,
    string Heading,
    IReadOnlyList<string> Blocks);

public sealed record ResumeFactInventory(
    IReadOnlyList<ResumeSectionInventory> Sections,
    IReadOnlyList<ResumeFact> Facts);

public sealed record ResumeIntegrityResult(
    IReadOnlyList<ResumeFact> PreservedFacts,
    IReadOnlyList<ResumeFact> MissingFacts,
    IReadOnlyList<ResumeFact> UnsupportedFacts,
    IReadOnlyList<ResumeSectionKind> MissingSections)
{
    public bool IsVerified => MissingFacts.Count == 0 && UnsupportedFacts.Count == 0 && MissingSections.Count == 0;
}

public sealed record ResumeFixResult(
    byte[] Bytes,
    IReadOnlyList<ResumeFixOutcome> Outcomes,
    ResumeGenerationStatus Status = ResumeGenerationStatus.ReviewRecommended,
    ResumeIntegrityResult? Integrity = null)
{
    // Integrity warnings never suppress a usable download. The audit tells the
    // user what needs review or manual correction.
    public bool CanDownloadGenerated => Bytes.Length > 0;
}
