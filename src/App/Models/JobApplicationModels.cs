namespace ProfessionalHub.ResumeTools.Models;

public sealed record AppliedJobRecord(
    string Fingerprint,
    string ProviderJobId,
    string Title,
    string Company,
    string Location,
    string Url,
    string Source,
    string PostedAt,
    string PostedDate,
    string Description,
    string[] Skills,
    string Status,
    string RecordedAt,
    string ResumeFileName,
    string ResumeBase64);

public sealed record AppliedJobReview(
    string Fingerprint,
    string ProviderJobId,
    string Title,
    string Company,
    string Location,
    string Url,
    string Source,
    string PostedAt,
    string PostedDate,
    string[] Skills,
    string Status,
    string RecordedAt,
    bool HasResume,
    string ResumeFileName);

public sealed record StoredJobResume(string FileName, string Base64);

public sealed record JobFolderState(bool Supported, bool Configured, bool PermissionGranted, string Message);

public sealed record JobSaveResult(int Saved, int Updated, int Total, string Message);
