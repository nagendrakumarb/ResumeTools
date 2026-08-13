using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Net.Client;
using ProfessionalHub.AI.Contracts.Grpc;

namespace ProfessionalHub.AI.Orchestrator;

public sealed record PortablePackageRequest(
    string RequestId,
    string TaskId,
    string PackageId,
    string Version,
    string WorkerEndpoint,
    string InputPath,
    string WorkingArtifactPath,
    string Runtime,
    string EntryPoint,
    double MinimumMetric = 0,
    int MaximumPackageMb = 25,
    IReadOnlyDictionary<string, string>? Options = null);

public sealed record PortablePackageFile(string Path, long Bytes, string Sha256);

public sealed record PortablePackageManifest(
    int SchemaVersion,
    string PackageId,
    string Version,
    string TaskId,
    string Runtime,
    string EntryPoint,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<PortablePackageFile> Files);

public sealed record PackageIndex(int SchemaVersion, DateTimeOffset GeneratedAtUtc, IReadOnlyList<PackageIndexEntry> Packages);
public sealed record PackageIndexEntry(string PackageId, string Version, string Manifest, string TaskId, string Runtime);
public sealed record PackagePipelineResult(bool Success, string Message, string? ManifestPath = null);

public sealed class PortablePackagePipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".onnx", ".zip", ".json", ".bin", ".txt", ".vocab", ".model" };

    public async Task<PackagePipelineResult> ExecuteAsync(
        string requestPath,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var requestFile = Path.GetFullPath(requestPath);
        if (!File.Exists(requestFile)) return new(false, $"Request file not found: {requestFile}");
        var request = JsonSerializer.Deserialize<PortablePackageRequest>(
            await File.ReadAllTextAsync(requestFile, cancellationToken), JsonOptions);
        if (request is null) return new(false, "The package request is empty or invalid.");

        var validation = ValidateRequest(request);
        if (validation is not null) return new(false, validation);

        var workingPath = Path.GetFullPath(request.WorkingArtifactPath);
        Directory.CreateDirectory(workingPath);
        await RequestWorkerAsync(request, workingPath, cancellationToken);

        var sourceFiles = Directory.EnumerateFiles(workingPath, "*", SearchOption.AllDirectories)
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sourceFiles.Count == 0)
            return new(false, "The worker completed without producing a portable model or data artifact.");

        var totalBytes = sourceFiles.Sum(path => new FileInfo(path).Length);
        if (totalBytes > request.MaximumPackageMb * 1024L * 1024L)
            return new(false, $"Package is {totalBytes / 1024d / 1024d:N1} MB; maximum is {request.MaximumPackageMb} MB.");

        var destinationRoot = Path.GetFullPath(outputRoot);
        var packageDirectory = Path.Combine(destinationRoot, request.PackageId, request.Version);
        if (Directory.Exists(packageDirectory)) Directory.Delete(packageDirectory, recursive: true);
        Directory.CreateDirectory(packageDirectory);

        var files = new List<PortablePackageFile>();
        foreach (var source in sourceFiles)
        {
            var relative = Path.GetRelativePath(workingPath, source).Replace('\\', '/');
            var destination = Path.GetFullPath(Path.Combine(packageDirectory, relative));
            EnsureInside(packageDirectory, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            await using var stream = File.OpenRead(destination);
            files.Add(new(relative, stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant()));
        }

        if (!files.Any(file => file.Path.Equals(request.EntryPoint, StringComparison.OrdinalIgnoreCase)))
            return new(false, $"Required entry point '{request.EntryPoint}' was not produced.");

        var manifest = new PortablePackageManifest(1, request.PackageId, request.Version, request.TaskId,
            request.Runtime, request.EntryPoint, DateTimeOffset.UtcNow,
            new Dictionary<string, double>(), files);
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await UpdateIndexAsync(destinationRoot, manifest, cancellationToken);
        return new(true, $"Published {request.PackageId} {request.Version} with {files.Count} validated file(s).", manifestPath);
    }

    private static async Task RequestWorkerAsync(PortablePackageRequest request, string workingPath, CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(request.WorkerEndpoint);
        var client = new AiArtifactWorker.AiArtifactWorkerClient(channel);
        var capabilities = await client.GetCapabilitiesAsync(new CapabilityRequest { Caller = "package-pipeline" }, cancellationToken: cancellationToken);
        if (!capabilities.Success || !capabilities.TaskIds.Contains(request.TaskId))
            throw new InvalidOperationException($"Worker '{capabilities.WorkerId}' does not support '{request.TaskId}'.");

        var task = new TaskRequest { TaskId = request.TaskId, InputPath = request.InputPath, OutputPath = workingPath };
        if (request.Options is not null)
            foreach (var option in request.Options)
                task.Options[option.Key] = option.Value;
        var execution = await client.ExecuteTaskAsync(task, cancellationToken: cancellationToken);
        if (!execution.Success) throw new InvalidOperationException(execution.Message);

        var export = await client.ExportPortablePackageAsync(new ExportRequest
        {
            TaskId = request.TaskId,
            SourceArtifactPath = string.IsNullOrWhiteSpace(execution.ArtifactPath) ? workingPath : execution.ArtifactPath,
            PortableOutputPath = workingPath,
            PackageVersion = request.Version
        }, cancellationToken: cancellationToken);
        if (!export.Success) throw new InvalidOperationException(export.Message);
        if (export.Metrics.Count > 0 && request.MinimumMetric > 0 && export.Metrics.Values.Max() < request.MinimumMetric)
            throw new InvalidOperationException($"Worker metric did not meet the requested minimum {request.MinimumMetric:0.###}.");
    }

    private static string? ValidateRequest(PortablePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.TaskId) ||
            string.IsNullOrWhiteSpace(request.PackageId) || string.IsNullOrWhiteSpace(request.Version))
            return "RequestId, TaskId, PackageId and Version are required.";
        if (!Uri.TryCreate(request.WorkerEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            return "WorkerEndpoint must be an absolute HTTP or HTTPS address.";
        if (request.MaximumPackageMb is < 1 or > 200) return "MaximumPackageMb must be between 1 and 200.";
        if (request.PackageId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || request.Version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "PackageId or Version contains invalid path characters.";
        return null;
    }

    private static async Task UpdateIndexAsync(string outputRoot, PortablePackageManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputRoot);
        var path = Path.Combine(outputRoot, "manifest.json");
        PackageIndex? current = null;
        if (File.Exists(path))
            current = JsonSerializer.Deserialize<PackageIndex>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        var entries = (current?.Packages ?? [])
            .Where(entry => !entry.PackageId.Equals(manifest.PackageId, StringComparison.OrdinalIgnoreCase))
            .Append(new PackageIndexEntry(manifest.PackageId, manifest.Version,
                $"{manifest.PackageId}/{manifest.Version}/manifest.json", manifest.TaskId, manifest.Runtime))
            .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new PackageIndex(1, DateTimeOffset.UtcNow, entries), JsonOptions), cancellationToken);
    }

    private static void EnsureInside(string root, string candidate)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Artifact path escaped the package directory.");
    }
}
