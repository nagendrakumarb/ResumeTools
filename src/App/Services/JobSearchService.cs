using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed class JobSearchService(HttpClient httpClient)
{
    public async Task<IReadOnlyList<NormalizedJob>> SearchLocalProvidersAsync(
        string configurationJson,
        JobSearchRequest request,
        IReadOnlyCollection<string> selectedSources,
        CancellationToken cancellationToken = default)
    {
        var root = JsonNode.Parse(configurationJson, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        })?.AsObject() ?? throw new InvalidDataException("The selected provider configuration is invalid.");
        var providers = root["providers"]?.AsArray().OfType<JsonObject>()
            .Where(provider => provider["enabled"]?.GetValue<bool?>() is not false)
            .Where(provider => selectedSources.Contains(
                provider["name"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase))
            .ToList() ?? [];
        if (providers.Count == 0)
            throw new InvalidOperationException("None of the selected sources is enabled in the local provider file.");

        var jobs = new List<NormalizedJob>();
        var failures = new List<string>();
        foreach (var provider in providers)
        {
            var name = provider["name"]?.GetValue<string>() ?? "Provider";
            var key = provider["apiKey"]?.GetValue<string>() ?? "";
            try
            {
                var providerRequest = request with { ApiKey = key };
                var found = (provider["type"]?.GetValue<string>() ?? "").ToLowerInvariant() switch
                {
                    "theirstack" => await SearchTheirStackAsync(providerRequest, cancellationToken),
                    "serpapi" => await SearchSerpApiAsync(providerRequest, cancellationToken),
                    "generic" => await SearchGenericProviderAsync(provider, providerRequest, cancellationToken),
                    _ => throw new NotSupportedException("This local provider type is not supported for live browser search.")
                };
                jobs.AddRange(found);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {BrowserSafeMessage(ex)}");
            }
        }
        if (jobs.Count == 0 && failures.Count > 0)
            throw new HttpRequestException(string.Join(" ", failures));
        return jobs.GroupBy(job => string.IsNullOrWhiteSpace(job.Url)
                ? $"{job.Title}|{job.Company}|{job.Location}"
                : job.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).Take(100).ToList();
    }

    public async Task<IReadOnlyList<NormalizedJob>> SearchAsync(JobSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword) &&
            request.Provider is not (JobApiProvider.JsonEndpoint or JobApiProvider.Catalog))
            throw new ArgumentException("Enter a job title, skill, or keyword.");

        return request.Provider switch
        {
            JobApiProvider.Catalog => await SearchCatalogAsync(request, cancellationToken),
            JobApiProvider.SerpApi => await SearchSerpApiAsync(request, cancellationToken),
            JobApiProvider.TheirStack => await SearchTheirStackAsync(request, cancellationToken),
            JobApiProvider.JsonEndpoint => await SearchJsonEndpointAsync(request, cancellationToken),
            _ => throw new NotSupportedException("The selected job provider is not supported.")
        };
    }

    public async Task<JobCatalogInfo> GetCatalogInfoAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await httpClient.GetFromJsonAsync<JobCatalog>("data/jobs.json", cancellationToken) ?? new JobCatalog();
        var providers = catalog.Providers.Count > 0
            ? catalog.Providers
            : catalog.Jobs.Select(job => job.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new JobCatalogInfo(catalog.GeneratedAt, catalog.Provider, catalog.Jobs.Count, providers,
            catalog.Keywords, catalog.Locations, catalog.Routing);
    }

    private async Task<IReadOnlyList<NormalizedJob>> SearchCatalogAsync(JobSearchRequest request, CancellationToken cancellationToken)
    {
        var catalog = await httpClient.GetFromJsonAsync<JobCatalog>("data/jobs.json", cancellationToken) ?? new JobCatalog();
        var roles = (request.Keyword ?? "").Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var locations = (request.Location ?? "").Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectedSources = (request.Source ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return catalog.Jobs
            .Where(job => selectedSources.Length == 0 ||
                          selectedSources.Contains("All", StringComparer.OrdinalIgnoreCase) ||
                          selectedSources.Contains(job.Source, StringComparer.OrdinalIgnoreCase))
            .Where(job => (roles.Length == 0 || roles.Any(role => MatchesRole(job.Title, job.Description, role))) &&
                          (string.IsNullOrWhiteSpace(request.ExperienceLevel) ||
                           $"{job.Title} {JobTextSanitizer.RelevantDescription(job.Description)}".Contains(request.ExperienceLevel, StringComparison.OrdinalIgnoreCase)))
            .Where(job => locations.Length == 0 || locations.Any(location => MatchesLocation(job.Location, location)))
            .Take(30)
            .ToList();
    }

    private static bool MatchesRole(string title, string description, string role)
    {
        if (string.IsNullOrWhiteSpace(role) || title.Contains(role, StringComparison.OrdinalIgnoreCase))
            return true;
        var value = $"{title} {JobTextSanitizer.RelevantDescription(description)}";
        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "developer", "engineer", "specialist", "professional", "senior", "junior", "lead" };
        var tokens = System.Text.RegularExpressions.Regex.Matches(role.ToLowerInvariant(), @"[a-z0-9+#.]+")
            .Select(match => match.Value).Where(token => token.Length > 1 && !generic.Contains(token)).Distinct().ToArray();
        return tokens.Length == 0
            ? role.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase))
            : tokens.All(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesLocation(string jobLocation, string requestedLocation)
    {
        if (string.IsNullOrWhiteSpace(requestedLocation)) return true;
        var requested = requestedLocation.Trim();
        if (requested.Equals("Remote", StringComparison.OrdinalIgnoreCase))
            return jobLocation.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
                   jobLocation.Contains("anywhere", StringComparison.OrdinalIgnoreCase) ||
                   jobLocation.Contains("worldwide", StringComparison.OrdinalIgnoreCase);
        if (requested.Equals("Worldwide", StringComparison.OrdinalIgnoreCase))
            return true;
        return jobLocation.Contains(requested, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<NormalizedJob>> SearchSerpApiAsync(JobSearchRequest request, CancellationToken cancellationToken)
    {
        RequireKey(request);
        var query = Uri.EscapeDataString(string.Join(' ', new[] { request.Keyword, request.ExperienceLevel }.Where(x => !string.IsNullOrWhiteSpace(x))));
        var isRemote = request.Location.Equals("Remote", StringComparison.OrdinalIgnoreCase);
        var location = isRemote || request.Location.Equals("Worldwide", StringComparison.OrdinalIgnoreCase)
            ? ""
            : Uri.EscapeDataString(request.Location ?? "");
        var dateFilter = request.PostedWithinDays <= 1 ? "&chips=date_posted%3Atoday" :
            request.PostedWithinDays <= 3 ? "&chips=date_posted%3A3days" :
            request.PostedWithinDays <= 7 ? "&chips=date_posted%3Aweek" :
            request.PostedWithinDays <= 30 ? "&chips=date_posted%3Amonth" : "";
        var uri = $"https://serpapi.com/search.json?engine=google_jobs&q={query}&location={location}" +
                  dateFilter + (isRemote ? "&ltype=1" : "") +
                  $"&api_key={Uri.EscapeDataString(request.ApiKey)}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        return await ReadAndNormalizeAsync(response, "SerpApi", cancellationToken);
    }

    private async Task<IReadOnlyList<NormalizedJob>> SearchTheirStackAsync(JobSearchRequest request, CancellationToken cancellationToken)
    {
        RequireKey(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.theirstack.com/v1/jobs/search");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        var titles = request.Keyword.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var locations = request.Location.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var payload = new Dictionary<string, object>
        {
            ["job_title_or"] = titles,
            ["limit"] = 20,
            ["page"] = 0,
            ["posted_at_max_age_days"] = Math.Clamp(request.PostedWithinDays, 1, 365)
        };
        if (locations.Length > 0)
            payload["job_location_pattern_or"] = locations;
        var seniority = NormalizeSeniority(request.ExperienceLevel);
        if (seniority.Length > 0)
            payload["job_seniority_or"] = new[] { seniority };
        message.Content = JsonContent.Create(payload);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadAndNormalizeAsync(response, "TheirStack", cancellationToken);
    }

    private async Task<IReadOnlyList<NormalizedJob>> SearchJsonEndpointAsync(JobSearchRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Endpoint?.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter a complete HTTPS JSON endpoint.");
        using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadAndNormalizeAsync(response, endpoint.Host, cancellationToken);
    }

    private async Task<IReadOnlyList<NormalizedJob>> SearchGenericProviderAsync(
        JsonObject provider,
        JobSearchRequest request,
        CancellationToken cancellationToken)
    {
        var keyword = request.Keyword.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        var location = request.Location.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        var experience = System.Text.RegularExpressions.Regex.Match(request.ExperienceLevel ?? "", @"\d+").Value;
        var endpoint = (provider["endpoint"]?.GetValue<string>() ?? "")
            .Replace("{keyword}", Uri.EscapeDataString(keyword))
            .Replace("{location}", Uri.EscapeDataString(location))
            .Replace("{experience}", Uri.EscapeDataString(experience))
            .Replace("{days}", Math.Clamp(request.PostedWithinDays, 1, 365).ToString())
            .Replace("{timeFrame}", RapidApiTimeFrame(request.PostedWithinDays))
            .Replace("{limit}", "25")
            .Replace("{page}", "1");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            endpoint.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The provider endpoint is incomplete.");
        using var message = new HttpRequestMessage(
            new HttpMethod(provider["method"]?.GetValue<string>() ?? "GET"), uri);
        var authentication = provider["authentication"]?.GetValue<string>() ?? "bearer";
        if (authentication.Equals("rapidapi", StringComparison.OrdinalIgnoreCase))
        {
            message.Headers.TryAddWithoutValidation("X-RapidAPI-Key", request.ApiKey);
            message.Headers.TryAddWithoutValidation("X-RapidAPI-Host", provider["host"]?.GetValue<string>() ?? "");
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            if (authentication.Equals("api-key", StringComparison.OrdinalIgnoreCase))
                message.Headers.TryAddWithoutValidation(
                    provider["apiKeyHeader"]?.GetValue<string>() ?? "X-API-Key", request.ApiKey.Trim());
            else
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        if (message.Method != HttpMethod.Get)
        {
            var body = provider["body"]?.DeepClone() ?? new JsonObject();
            ReplaceJsonTokens(body, keyword, location, request.PostedWithinDays);
            message.Content = JsonContent.Create(body);
        }
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadAndNormalizeAsync(response, provider["name"]?.GetValue<string>() ?? uri.Host, cancellationToken);
    }

    private static void ReplaceJsonTokens(JsonNode node, string keyword, string location, int days)
    {
        if (node is JsonObject item)
            foreach (var property in item.ToList())
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    item[property.Key] = text.Replace("{keyword}", keyword).Replace("{location}", location)
                        .Replace("{days}", days.ToString()).Replace("{limit}", "25");
                else if (property.Value is not null)
                    ReplaceJsonTokens(property.Value, keyword, location, days);
        else if (node is JsonArray array)
            foreach (var child in array.Where(child => child is not null))
                ReplaceJsonTokens(child!, keyword, location, days);
    }

    private static string RapidApiTimeFrame(int days) =>
        days <= 1 ? "24h" :
        days <= 7 ? "7d" :
        "6m";

    private static string BrowserSafeMessage(Exception exception) =>
        exception is HttpRequestException && exception.Message.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase)
            ? "The browser blocked the provider request (CORS or network policy)."
            : exception.Message;

    private static string NormalizeSeniority(string value) => value.Trim().ToLowerInvariant() switch
    {
        "entry" or "entry level" or "junior" => "junior",
        "mid" or "mid level" or "mid-level" => "mid_level",
        "senior" => "senior",
        "staff" => "staff",
        "principal" => "principal",
        _ => ""
    };

    private static void RequireKey(JobSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new ArgumentException($"{request.Provider} requires your API key. It is kept only in this browser tab.");
    }

    private static async Task<IReadOnlyList<NormalizedJob>> ReadAndNormalizeAsync(
        HttpResponseMessage response,
        string source,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{source} returned {(int)response.StatusCode}: {ExtractError(json)}");
        using var document = JsonDocument.Parse(json);
        var items = FindJobArray(document.RootElement);
        var jobs = items.Select((item, index) => Normalize(item, source, index))
            .Where(job => !string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.Description))
            .Take(30)
            .ToList();
        if (jobs.Count == 0)
            throw new InvalidDataException("The provider returned JSON, but no job title and description fields could be normalized.");
        return jobs;
    }

    private static IEnumerable<JsonElement> FindJobArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToArray();
        foreach (var name in new[] { "jobs_results", "jobDetails", "data", "jobs", "results", "items" })
            if (TryGet(root, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().ToArray();
                if (value.ValueKind == JsonValueKind.Object)
                    foreach (var nested in new[] { "jobDetails", "jobs", "results", "items", "data" })
                        if (TryGet(value, nested, out var array) && array.ValueKind == JsonValueKind.Array)
                            return array.EnumerateArray().ToArray();
            }
        return [];
    }

    private static NormalizedJob Normalize(JsonElement item, string source, int index)
    {
        var title = Text(item, "title", "job_title", "position", "name");
        var company = Text(item, "company_name", "companyName", "company", "employer_name", "organization");
        var location = Text(item, "location", "job_location", "formatted_location", "city");
        var description = Text(item, "description", "jobDescription", "descriptionHtml", "job_description", "description_text", "content", "snippet", "keySkills");
        var url = Text(item, "final_url", "job_url", "jobUrl", "url", "source_url", "applyUrl", "apply_link", "share_link", "link");
        var posted = Text(item, "posted_at", "postedAt", "posted_date", "detected_extensions.posted_at", "date_posted");
        var id = Text(item, "job_id", "id", "htidocid");
        return new NormalizedJob(string.IsNullOrWhiteSpace(id) ? $"{source}-{index}" : id, title, company, location, description, url, posted, source);
    }

    private static string Text(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            var current = item;
            var found = true;
            foreach (var segment in name.Split('.'))
                if (!TryGet(current, segment, out current)) { found = false; break; }
            if (!found) continue;
            if (current.ValueKind == JsonValueKind.String) return current.GetString()?.Trim() ?? "";
            if (current.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return current.ToString();
            if (current.ValueKind == JsonValueKind.Array)
                return string.Join(", ", current.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()));
        }
        return "";
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Text(document.RootElement, "error", "message", "detail");
        }
        catch { return json.Length > 240 ? json[..240] : json; }
    }

    private sealed class JobCatalog
    {
        public DateTimeOffset? GeneratedAt { get; set; }
        public string Provider { get; set; } = "Not configured";
        public List<string> Providers { get; set; } = [];
        public List<string> Keywords { get; set; } = [];
        public List<string> Locations { get; set; } = [];
        public List<JobProviderState> Routing { get; set; } = [];
        public List<NormalizedJob> Jobs { get; set; } = [];
    }
}

public sealed record JobCatalogInfo(
    DateTimeOffset? GeneratedAt,
    string Provider,
    int JobCount,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Locations,
    IReadOnlyList<JobProviderState> Routing);

public sealed record JobProviderState(
    string Name,
    string Status,
    double? RemainingCredits,
    int JobsReturned,
    string Note);
