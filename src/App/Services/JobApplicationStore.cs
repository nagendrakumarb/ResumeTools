using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed partial class JobApplicationStore(IJSRuntime jsRuntime)
{
    public ValueTask<JobFolderState> GetStateAsync() =>
        jsRuntime.InvokeAsync<JobFolderState>("professionalHub.jobLedger.getState");

    public ValueTask<JobFolderState> ChooseFolderAsync() =>
        jsRuntime.InvokeAsync<JobFolderState>("professionalHub.jobLedger.chooseFolder");

    public ValueTask<string[]> LoadFingerprintsAsync() =>
        jsRuntime.InvokeAsync<string[]>("professionalHub.jobLedger.loadFingerprints");

    public ValueTask<AppliedJobReview[]> ListAsync() =>
        jsRuntime.InvokeAsync<AppliedJobReview[]>("professionalHub.jobLedger.list");

    public ValueTask<StoredJobResume> GetResumeAsync(string postedDate, string fingerprint) =>
        jsRuntime.InvokeAsync<StoredJobResume>("professionalHub.jobLedger.getResume", postedDate, fingerprint);

    public ValueTask<JobSaveResult> SaveAsync(
        IEnumerable<NormalizedJob> jobs,
        string status,
        byte[]? resumeBytes = null,
        string resumeFileName = "")
    {
        var resumeBase64 = resumeBytes is { Length: > 0 } ? Convert.ToBase64String(resumeBytes) : "";
        var records = jobs.Select(job => ToRecord(job, status, resumeFileName, resumeBase64)).ToArray();
        return jsRuntime.InvokeAsync<JobSaveResult>("professionalHub.jobLedger.save", records);
    }

    public static string Fingerprint(NormalizedJob job)
    {
        var identity = string.Join("|",
            Normalize(job.Source),
            Normalize(job.Id),
            Normalize(job.Url),
            Normalize(job.Title),
            Normalize(job.Company),
            Normalize(job.Location),
            NormalizeDate(job.PostedAt));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static AppliedJobRecord ToRecord(
        NormalizedJob job,
        string status,
        string resumeFileName,
        string resumeBase64)
    {
        var postedDate = NormalizeDate(job.PostedAt);
        return new AppliedJobRecord(
            Fingerprint(job), job.Id, job.Title, job.Company, job.Location, job.Url, job.Source,
            job.PostedAt, postedDate, job.Description, ExtractSkills(job), status,
            DateTimeOffset.UtcNow.ToString("O"), resumeFileName, resumeBase64);
    }

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(value ?? "", " ").Trim().ToLowerInvariant();

    private static string NormalizeDate(string value) =>
        DateTimeOffset.TryParse(value, out var date)
            ? date.UtcDateTime.ToString("yyyy-MM-dd")
            : DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd");

    private static string[] ExtractSkills(NormalizedJob job)
    {
        var text = $"{job.Title} {JobTextSanitizer.RelevantDescription(job.Description)}".ToLowerInvariant();
        var known = new[]
        {
            ".net", "asp.net", "c#", "java", "python", "javascript", "typescript", "node.js", "react", "angular",
            "azure", "aws", "gcp", "sql", "mongodb", "cosmos db", "redis", "docker", "kubernetes", "terraform",
            "microservices", "rest", "graphql", "devops", "ci/cd", "git", "machine learning", "data engineering",
            "dynamodb", "authentication", "authorization", "identity", "sdk", "observability", "incident response"
        };
        return known.Where(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
