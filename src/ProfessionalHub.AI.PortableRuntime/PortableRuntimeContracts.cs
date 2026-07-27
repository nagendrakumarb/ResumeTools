namespace ProfessionalHub.AI.PortableRuntime;

public sealed record PortablePackageManifest(
    string Id,
    string Version,
    string PackageType,
    string Task,
    string Runtime = "browser-wasm",
    string Sha256 = "");

public sealed record PortableInferenceRequest(
    string PackageId,
    string Text,
    IReadOnlyDictionary<string, string>? Context = null);

public sealed record PortableInferenceResult(
    bool Success,
    string PackageId,
    string Label,
    double Confidence,
    string Message,
    IReadOnlyDictionary<string, double>? Scores = null);

public interface IPortablePackageLoader
{
    ValueTask<PortablePackageManifest?> LoadManifestAsync(
        Stream manifest,
        CancellationToken cancellationToken = default);
}

public interface IPortableInferenceEngine
{
    ValueTask<PortableInferenceResult> PredictAsync(
        PortableInferenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PlaceholderPortableInferenceEngine : IPortableInferenceEngine
{
    public ValueTask<PortableInferenceResult> PredictAsync(
        PortableInferenceRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PortableInferenceResult(
            true,
            request.PackageId,
            "not-yet-implemented",
            1d,
            "Portable inference contract is available; implementation is intentionally pending.",
            new Dictionary<string, double> { ["placeholder"] = 1d }));
}
