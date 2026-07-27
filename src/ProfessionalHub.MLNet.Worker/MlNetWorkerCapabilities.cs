using ProfessionalHub.AI.Contracts.Abstractions;

namespace ProfessionalHub.MLNet.Worker;

public sealed class MlNetWorkerCapabilities : IAiCapabilityProvider
{
    public string WorkerId => "professionalhub-mlnet";
    public string Runtime => "ML.NET 5 / .NET 9";

    public IReadOnlyCollection<string> SupportedTaskIds { get; } =
    [
        AiTaskIds.RequirementClassification,
        AiTaskIds.MatchScoring,
        AiTaskIds.ResumeSectionClassification,
        AiTaskIds.ContentRetentionRanking,
        AiTaskIds.RoleClassification,
        AiTaskIds.JobRelevanceRanking,
        AiTaskIds.DuplicateJobDetection,
        AiTaskIds.SuspiciousJobDetection,
        AiTaskIds.AchievementQuality
    ];
}
