namespace ProfessionalHub.AI.Contracts.Abstractions;

public static class AiTaskIds
{
    public const string RequirementClassification = "requirement-classification";
    public const string MeaningfulPhraseExtraction = "meaningful-phrase-extraction";
    public const string ContextualTermPlacement = "contextual-term-placement";
    public const string MatchScoring = "match-scoring";
    public const string ResumeSectionClassification = "resume-section-classification";
    public const string DocumentIntegrityValidation = "document-integrity-validation";
    public const string ContentRetentionRanking = "content-retention-ranking";
    public const string RoleClassification = "role-classification";
    public const string JobRelevanceRanking = "job-relevance-ranking";
    public const string DuplicateJobDetection = "duplicate-job-detection";
    public const string SuspiciousJobDetection = "suspicious-job-detection";
    public const string AchievementQuality = "achievement-quality";
    public const string EvidenceGroundedRewrite = "evidence-grounded-rewrite";
    public const string TemplateLayoutUnderstanding = "template-layout-understanding";
    public const string CorrectionRiskPrediction = "correction-risk-prediction";
    public const string ModelQualityMonitoring = "model-quality-monitoring";

    public static IReadOnlyList<string> All { get; } =
    [
        RequirementClassification,
        MeaningfulPhraseExtraction,
        ContextualTermPlacement,
        MatchScoring,
        ResumeSectionClassification,
        DocumentIntegrityValidation,
        ContentRetentionRanking,
        RoleClassification,
        JobRelevanceRanking,
        DuplicateJobDetection,
        SuspiciousJobDetection,
        AchievementQuality,
        EvidenceGroundedRewrite,
        TemplateLayoutUnderstanding,
        CorrectionRiskPrediction,
        ModelQualityMonitoring
    ];
}

public sealed record AiTaskContext(
    string TaskId,
    string InputPath = "",
    string OutputPath = "",
    IReadOnlyDictionary<string, string>? Options = null);

public sealed record AiTaskResult(
    bool Success,
    string TaskId,
    string Message,
    string ArtifactPath = "",
    string PackageType = "placeholder",
    IReadOnlyDictionary<string, double>? Metrics = null)
{
    public static AiTaskResult Completed(string taskId, string message, string artifactPath = "") =>
        new(true, taskId, message, artifactPath, "placeholder",
            new Dictionary<string, double> { ["placeholder"] = 1d });
}

public interface IAiTask
{
    string TaskId { get; }
    string IdentifiedIssue { get; }
    string IntendedOutcome { get; }
    ValueTask<AiTaskResult> ExecuteAsync(AiTaskContext context, CancellationToken cancellationToken = default);
}

public interface IRequirementClassificationTask : IAiTask;
public interface IMeaningfulPhraseExtractionTask : IAiTask;
public interface IContextualTermPlacementTask : IAiTask;
public interface IMatchScoringTask : IAiTask;
public interface IResumeSectionClassificationTask : IAiTask;
public interface IDocumentIntegrityValidationTask : IAiTask;
public interface IContentRetentionRankingTask : IAiTask;
public interface IRoleClassificationTask : IAiTask;
public interface IJobRelevanceRankingTask : IAiTask;
public interface IDuplicateJobDetectionTask : IAiTask;
public interface ISuspiciousJobDetectionTask : IAiTask;
public interface IAchievementQualityTask : IAiTask;

public interface IAiCapabilityProvider
{
    string WorkerId { get; }
    string Runtime { get; }
    IReadOnlyCollection<string> SupportedTaskIds { get; }
}

public interface IPortableArtifactExporter
{
    ValueTask<AiTaskResult> ExportAsync(
        AiTaskContext context,
        string sourceArtifactPath,
        CancellationToken cancellationToken = default);
}
