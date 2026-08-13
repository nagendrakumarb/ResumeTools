using ProfessionalHub.AI.Contracts.Abstractions;

namespace ProfessionalHub.DotNetAI.Worker.Features;

/// <summary>
/// Base for transformer/ONNX and controlled-generation feature scaffolds.
/// Model execution belongs in this local worker; only compact, browser-safe artifacts are exported.
/// </summary>
public abstract class TransformerFeatureScaffold : IDataScienceFeature
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
/// Feature: extract meaningful skills, qualifications and domain phrases—not filler words such as
/// "typically", "desired" or "minimum".
/// Technique: transformer token classification/keyphrase extraction.
/// Recommended implementation: fine-tune BERT/Sentence-BERT on labelled job phrases; retain phrase
/// boundaries; classify phrase type; reject low-confidence/noise phrases; evaluate phrase-level F1;
/// export quantized ONNX plus tokenizer and label map for ONNX Runtime Web.
/// </summary>
public sealed class MeaningfulPhraseExtractionFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.MeaningfulPhraseExtraction;
    public override string Technique => "Transformer token classification and keyphrase extraction";
    public override string RecommendedImplementation => "Fine-tuned BERT exported to quantized ONNX with phrase-level F1 validation";
}

/// <summary>
/// Feature: compare resume evidence with job requirements by meaning rather than exact words.
/// Technique: Sentence-BERT embeddings, cosine similarity and calibrated requirement coverage.
/// Recommended implementation: encode complete phrases with a compact sentence-transformer;
/// calibrate similarity thresholds on labelled resume/job pairs; report required and preferred
/// coverage separately; export quantized ONNX, tokenizer and calibration.json. A 100% score means
/// every classified requirement has supported evidence—not that every word was copied.
/// </summary>
public sealed class MatchScoringFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.MatchScoring;
    public override string Technique => "Sentence embeddings plus calibrated semantic similarity";
    public override string RecommendedImplementation => "Sentence-BERT ONNX; labelled pair calibration; requirement-weighted coverage";
}

/// <summary>
/// Feature: choose the most truthful section and paragraph for a supported job term.
/// Technique: cross-encoder classification or learning-to-rank over candidate placements.
/// Recommended implementation: generate candidates from classified resume sections; score
/// (term, evidence, section, neighbouring text) with BERT; require an evidence threshold; evaluate
/// top-1 placement accuracy and unsupported-placement rate; export ONNX plus thresholds.json.
/// If evidence is absent, return Manual review instead of inserting the term into Skills.
/// </summary>
public sealed class ContextualTermPlacementFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.ContextualTermPlacement;
    public override string Technique => "Cross-encoder candidate ranking with evidence gating";
    public override string RecommendedImplementation => "BERT placement ranker exported to ONNX; top-1 accuracy and unsupported-rate checks";
}

/// <summary>
/// Feature: detect duplicate jobs even when titles/descriptions differ slightly.
/// Technique: semantic record linkage and clustering.
/// Recommended implementation: combine normalized stable fields with Sentence-BERT embeddings;
/// train/calibrate a pair classifier on duplicate/non-duplicate job pairs; evaluate pairwise F1;
/// export ONNX and matching-thresholds.json. Provider job ID remains the strongest exact signal.
/// </summary>
public sealed class DuplicateJobDetectionFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.DuplicateJobDetection;
    public override string Technique => "Semantic record linkage using sentence embeddings";
    public override string RecommendedImplementation => "Sentence-BERT pair classifier/embedding model with calibrated duplicate threshold";
}

/// <summary>
/// Feature: verify that a corrected or template-converted resume retains facts and sections.
/// Technique: natural-language inference (entailment/contradiction) plus entity alignment.
/// Recommended implementation: classify source/result sentence pairs with an NLI model; align names,
/// employers, dates, qualifications and metrics; calculate missing, changed and unsupported claims;
/// evaluate contradiction recall; export quantized ONNX and entity schema. Findings never block the
/// user's download—the UI exposes the audit and lets the user correct it manually.
/// </summary>
public sealed class DocumentIntegrityValidationFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.DocumentIntegrityValidation;
    public override string Technique => "Natural-language inference plus named-entity alignment";
    public override string RecommendedImplementation => "Compact NLI BERT ONNX with entity comparison and contradiction-recall evaluation";
}

/// <summary>
/// Feature: produce a grammatical rewrite only from supplied evidence.
/// Technique: retrieval-augmented constrained generation.
/// Recommended implementation: run a local quantized Mistral/Llama model in the Python or .NET
/// producer; pass only selected evidence and an output schema; validate the draft with the integrity
/// model; reject unsupported claims; persist only the approved text/rules artifact. Large generators
/// are not deployed to GitHub Pages unless a suitably small browser model is proven viable.
/// </summary>
public sealed class EvidenceGroundedRewriteFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.EvidenceGroundedRewrite;
    public override string Technique => "Retrieval-augmented constrained generation with NLI validation";
    public override string RecommendedImplementation => "Local quantized Mistral/Llama producer; schema-constrained output; NLI fact gate";
}

/// <summary>
/// Feature: understand rows, columns, headings and reading order in an uploaded template image/PDF.
/// Technique: document-layout transformer and object detection.
/// Recommended implementation: fine-tune LayoutLMv3 on labelled bounding boxes and semantic roles;
/// export ONNX plus label-map.json; reconstruct a layout specification rather than copying pixels;
/// measure region mAP, reading-order accuracy and section-placement accuracy. The generated DOCX
/// must pass the document-integrity audit before it is offered as a faithful conversion.
/// </summary>
public sealed class TemplateLayoutUnderstandingFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.TemplateLayoutUnderstanding;
    public override string Technique => "LayoutLMv3 document understanding and layout object detection";
    public override string RecommendedImplementation => "LayoutLMv3 ONNX with labelled regions, reading-order evaluation and integrity audit";
}

/// <summary>
/// Feature: predict whether a proposed correction is likely to damage formatting or factual meaning.
/// Technique: calibrated binary risk classification.
/// Recommended implementation: label past edits Safe/Risky using before/after structure, style and
/// integrity features; train a calibrated classifier; optimize recall for risky edits; export ONNX
/// or model.zip and risk-thresholds.json. High risk means show a warning, never forbid downloading.
/// </summary>
public sealed class CorrectionRiskPredictionFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.CorrectionRiskPrediction;
    public override string Technique => "Calibrated binary risk classification";
    public override string RecommendedImplementation => "BERT/ML.NET classifier using before-after features; high risky-edit recall";
}

/// <summary>
/// Feature: detect when production inputs differ from training data and a model needs retraining.
/// Technique: model evaluation, confidence monitoring and data-drift detection.
/// Recommended implementation: store privacy-safe aggregate feature distributions and labelled
/// evaluation samples; compare PSI/Jensen-Shannon divergence, confidence and task metrics against
/// model-card thresholds; emit model-health.json. Do not upload resumes or personal data.
/// </summary>
public sealed class ModelQualityMonitoringFeature : TransformerFeatureScaffold
{
    public override string FeatureId => AiTaskIds.ModelQualityMonitoring;
    public override string Technique => "Data-drift detection and offline model evaluation";
    public override string RecommendedImplementation => "PSI/Jensen-Shannon drift plus task metrics recorded in model-health.json";
}
