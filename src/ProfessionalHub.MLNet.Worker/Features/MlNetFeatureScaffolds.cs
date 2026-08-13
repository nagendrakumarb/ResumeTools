using ProfessionalHub.AI.Contracts.Abstractions;

namespace ProfessionalHub.MLNet.Worker.Features;

/// <summary>
/// Base for ML.NET feature scaffolds. It intentionally returns a successful "planned" result
/// until a trained artifact is supplied, so unfinished learning work never crashes the worker
/// or masquerades as a real prediction.
/// </summary>
public abstract class MlNetFeatureScaffold : IDataScienceFeature
{
    public abstract string FeatureId { get; }
    public abstract string Technique { get; }
    public abstract string RecommendedImplementation { get; }

    public virtual ValueTask<DataScienceFeatureResult> RunAsync(
        DataScienceFeatureRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(DataScienceFeatureResult.Planned(FeatureId, RecommendedImplementation));
}

/// <summary>
/// Feature: classify each job-description sentence as Required skill, Preferred skill,
/// Responsibility, Qualification, Benefit, Company text, or Noise.
/// Technique: supervised multiclass text classification.
/// Recommended implementation: create human-labelled sentences; use ML.NET FeaturizeText plus
/// LightGbmMulticlass (SDCA is a simpler baseline); split data by job advert, not random sentence,
/// to prevent leakage; measure macro-F1 and per-class recall; export model.zip. If the baseline
/// misses context, train a BERT classifier outside Blazor and export ONNX for browser inference.
/// Effect on app: only Required/Preferred evidence is used for match scoring and missing terms.
/// </summary>
public sealed class RequirementClassificationFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.RequirementClassification;
    public override string Technique => "Supervised multiclass sentence classification";
    public override string RecommendedImplementation => "ML.NET LightGBM/SDCA baseline; BERT ONNX upgrade; macro-F1 evaluation";
}

/// <summary>
/// Feature: identify Summary, Experience, Skills, Education, Projects, Certifications and Other.
/// Technique: supervised multiclass sequence/paragraph classification.
/// Recommended implementation: label paragraphs together with style signals (order, heading flag,
/// font size and neighbouring labels); train LightGbmMulticlass; evaluate by document-level split;
/// export model.zip plus label-map.json. Use LayoutLM ONNX when visual position is essential.
/// Effect on app: corrections are placed in the right section without hard-coded resume layouts.
/// </summary>
public sealed class ResumeSectionClassificationFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.ResumeSectionClassification;
    public override string Technique => "Multiclass paragraph classification with layout features";
    public override string RecommendedImplementation => "ML.NET LightGBM baseline; LayoutLM ONNX for complex PDF/DOCX layouts";
}

/// <summary>
/// Feature: score an achievement bullet from weak to excellent.
/// Technique: ordinal classification or bounded regression.
/// Recommended implementation: label bullets 0-4 using action, scope, outcome and evidence rubric;
/// train LightGbmRegression, calibrate to 0-100, evaluate MAE and rank correlation, and export
/// model.zip. Never infer or invent metrics; unsupported facts remain a manual-review finding.
/// </summary>
public sealed class AchievementQualityFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.AchievementQuality;
    public override string Technique => "Ordinal quality scoring / regression";
    public override string RecommendedImplementation => "ML.NET LightGBM regression with rubric-labelled bullets and MAE evaluation";
}

/// <summary>
/// Feature: rank paragraphs for safe compaction while preserving every section and all facts.
/// Technique: learning-to-rank.
/// Recommended implementation: label Keep, Condense, or Manual-review examples; group rows by
/// resume; train ML.NET LightGbmRanking; validate retained facts/sections before page-layout changes;
/// export model.zip and thresholds.json. The ranker proposes priorities—it must never delete content.
/// </summary>
public sealed class ContentRetentionRankingFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.ContentRetentionRanking;
    public override string Technique => "Learning-to-rank for content retention and compaction";
    public override string RecommendedImplementation => "ML.NET LightGBM ranking grouped by resume; integrity gate before download";
}

/// <summary>
/// Feature: classify a resume/job into a hierarchy such as Technical > Software > Backend.
/// Technique: hierarchical multiclass classification.
/// Recommended implementation: train a coarse category model followed by a specialist role model;
/// use title, summary, skills and recent experience; measure macro-F1 at both hierarchy levels;
/// export model.zip and taxonomy.json. Low-confidence predictions return Unknown, not a forced role.
/// </summary>
public sealed class RoleClassificationFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.RoleClassification;
    public override string Technique => "Hierarchical multiclass classification";
    public override string RecommendedImplementation => "Two-stage ML.NET LightGBM classifier with calibrated confidence and taxonomy.json";
}

/// <summary>
/// Feature: order fetched jobs by relevance to the user's resume and preferences.
/// Technique: supervised learning-to-rank.
/// Recommended implementation: build query groups per user/search, label relevance from explicit
/// selections and applications, train LightGbmRanking, evaluate NDCG@10, and export model.zip.
/// Personal training data stays local; never train from sensitive resume text without consent.
/// </summary>
public sealed class JobRelevanceRankingFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.JobRelevanceRanking;
    public override string Technique => "Learning-to-rank";
    public override string RecommendedImplementation => "ML.NET LightGBM ranking with query groups and NDCG@10 evaluation";
}

/// <summary>
/// Feature: flag suspicious or scam-like job adverts without declaring fraud as fact.
/// Technique: calibrated binary classification with optional anomaly score.
/// Recommended implementation: label verified legitimate/suspicious adverts; train LightGBM binary;
/// handle class imbalance; evaluate precision-recall AUC and false-positive rate; export model.zip.
/// UI wording must say "risk signal" and show reasons plus a manual verification link.
/// </summary>
public sealed class SuspiciousJobDetectionFeature : MlNetFeatureScaffold
{
    public override string FeatureId => AiTaskIds.SuspiciousJobDetection;
    public override string Technique => "Calibrated binary classification and anomaly detection";
    public override string RecommendedImplementation => "ML.NET LightGBM binary classifier; PR-AUC and false-positive evaluation";
}
