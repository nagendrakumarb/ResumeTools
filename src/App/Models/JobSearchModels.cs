namespace ProfessionalHub.ResumeTools.Models;

public enum JobApiProvider
{
    Catalog,
    SerpApi,
    TheirStack,
    JsonEndpoint
}

public sealed record JobSearchRequest(
    JobApiProvider Provider,
    string Keyword,
    string Location,
    string ExperienceLevel,
    int PostedWithinDays,
    string ApiKey,
    string Endpoint,
    string Source = "");

public sealed record NormalizedJob(
    string Id,
    string Title,
    string Company,
    string Location,
    string Description,
    string Url,
    string PostedAt,
    string Source);
