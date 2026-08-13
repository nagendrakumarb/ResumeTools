namespace ProfessionalHub.AI.Contracts.Abstractions;

/// <summary>
/// Input shared by the training and inference feature scaffolds.
/// Keep raw documents out of portable artifacts: workers receive local text or local paths,
/// while the browser receives only an exported model and its metadata.
/// </summary>
public sealed record DataScienceFeatureRequest(
    string InputText = "",
    string ComparisonText = "",
    string InputPath = "",
    string ModelPath = "",
    IReadOnlyDictionary<string, string>? Options = null);

/// <summary>
/// Safe result returned while a feature is still being learned and implemented.
/// A scaffold reports success without pretending that an untrained model made a prediction.
/// </summary>
public sealed record DataScienceFeatureResult(
    bool Success,
    bool ModelReady,
    string FeatureId,
    string Message,
    IReadOnlyDictionary<string, double>? Scores = null,
    IReadOnlyList<string>? Labels = null,
    string ArtifactPath = "")
{
    public static DataScienceFeatureResult Planned(string featureId, string methodology) =>
        new(true, false, featureId,
            $"Feature scaffold is ready. Train, evaluate, and export the recommended model: {methodology}",
            new Dictionary<string, double>(), Array.Empty<string>());
}

/// <summary>
/// Common contract for every data-science feature. Implementations must not silently claim
/// a prediction when ModelReady is false.
/// </summary>
public interface IDataScienceFeature
{
    string FeatureId { get; }
    string Technique { get; }
    string RecommendedImplementation { get; }

    ValueTask<DataScienceFeatureResult> RunAsync(
        DataScienceFeatureRequest request,
        CancellationToken cancellationToken = default);
}
