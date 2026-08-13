using ProfessionalHub.AI.Contracts.Abstractions;

namespace ProfessionalHub.DotNetAI.Worker;

public sealed class DotNetWorkerCapabilities : IAiCapabilityProvider
{
    public string WorkerId => "professionalhub-dotnet-ai";
    public string Runtime => ".NET 9";

    public IReadOnlyCollection<string> SupportedTaskIds { get; } =
    [
        AiTaskIds.MeaningfulPhraseExtraction,
        AiTaskIds.ContextualTermPlacement,
        AiTaskIds.MatchScoring,
        AiTaskIds.ContentRetentionRanking,
        AiTaskIds.RoleClassification,
        AiTaskIds.JobRelevanceRanking,
        AiTaskIds.DuplicateJobDetection,
        AiTaskIds.SuspiciousJobDetection,
        AiTaskIds.AchievementQuality,
        AiTaskIds.DocumentIntegrityValidation,
        AiTaskIds.EvidenceGroundedRewrite,
        AiTaskIds.TemplateLayoutUnderstanding,
        AiTaskIds.CorrectionRiskPrediction,
        AiTaskIds.ModelQualityMonitoring
    ];
}
