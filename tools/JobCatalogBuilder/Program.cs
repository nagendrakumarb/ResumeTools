using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string configFile = "src/App/job-providers.json";
const string searchOptionsFile = "src/App/wwwroot/data/job-search-options.json";
const string outputFile = "src/App/wwwroot/data/jobs.json";

if (!File.Exists(configFile))
{
    Console.WriteLine($"{configFile} was not found; retaining the checked-in empty catalog.");
    return;
}

var root = JsonNode.Parse(await File.ReadAllTextAsync(configFile), documentOptions: new JsonDocumentOptions
{
    CommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
})?.AsObject()
    ?? throw new InvalidDataException($"{configFile} is invalid.");
var catalog = root["catalog"]?.AsObject() ?? new JsonObject();
var searchOptions = File.Exists(searchOptionsFile)
    ? JsonNode.Parse(await File.ReadAllTextAsync(searchOptionsFile), documentOptions: new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    })?.AsObject() ?? new JsonObject()
    : new JsonObject();
var keywords = StringArray(searchOptions, "keywords");
if (keywords.Count == 0)
    keywords = All(searchOptions, "roleGroups")
        .SelectMany(group => StringArray(group, "roles"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
var locations = StringArray(searchOptions, "locations");
var searches = (from keyword in keywords
                from location in locations.DefaultIfEmpty("")
                select new Search(keyword, location)).ToList();
var limit = Number(catalog, "limitPerSearch", 25);
var days = Number(catalog, "postedWithinDays", 30);
var target = Number(catalog, "minimumCatalogJobs", 75);
var jobs = new List<Job>();
var reports = new List<ProviderReport>();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ProfessionalHub-JobCatalog/1.0 (+https://professionalhub.co.in)");

foreach (var source in All(root, "freeSources").Where(x => !Bool(x, "enabled", true)))
    reports.Add(new(Name(source), "disabled", null, 0, "Disabled in configuration"));
foreach (var provider in All(root, "providers").Where(x => !Bool(x, "enabled", true)))
    reports.Add(new(Name(provider), "disabled", null, 0, "Disabled in configuration"));
foreach (var fallback in All(root, "publicWebFallbacks").Where(x => !Bool(x, "enabled", true)))
    reports.Add(new(Name(fallback), "disabled", null, 0, "Disabled in configuration"));
foreach (var fallback in Enabled(root, "publicWebFallbacks"))
    reports.Add(HasPlaceholderEndpoint(fallback)
        ? new(Name(fallback), "not-configured", null, 0, "Enabled, but the board token or careers URL is incomplete")
        : new(Name(fallback), "available", null, 0, "Configured fallback source"));

foreach (var source in Enabled(root, "freeSources"))
{
    try
    {
        var sourceJobs = await ReadFreeSource(source);
        var filteredJobs = Filter(sourceJobs);
        Console.WriteLine($"{Name(source)}: {sourceJobs.Count} received, {filteredJobs.Count} matched configured searches.");
        jobs.AddRange(filteredJobs);
        reports.Add(new(Name(source), "free", null, sourceJobs.Count, "Public no-key source"));
    }
    catch (Exception ex)
    {
        reports.Add(new(Name(source), "failed", null, 0, Short(ex.Message)));
    }
}

var metered = new List<Metered>();
foreach (var provider in Enabled(root, "providers"))
{
    var key = Text(provider, "apiKey");
    if (string.IsNullOrWhiteSpace(key) || key.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase) ||
        HasPlaceholderConfiguration(provider))
    {
        reports.Add(new(Name(provider), "not-configured", null, 0,
            "Enabled, but the provider-specific endpoint, host, actor, or API key is incomplete"));
        continue;
    }
    try
    {
        var remaining = await Remaining(provider);
        metered.Add(new(provider, remaining, Number(provider, "maxCallsPerRun", searches.Count)));
        if (remaining is not null && remaining < Number(provider, "minimumRemainingCredits", 1))
            reports.Add(new(Name(provider), "exhausted", remaining, 0, "Insufficient credits for another search"));
        else
            reports.Add(new(Name(provider), "available", remaining, 0,
                remaining is null ? "Configured for manual override and automatic routing" : "Configured with available credits"));
    }
    catch (Exception ex)
    {
        reports.Add(new(Name(provider), "balance-check-failed", null, 0,
            $"Could not verify credits ({StatusCode(ex)}). Check the API key and provider account."));
    }
}

// Rotate equal-capacity providers by UTC day, then continuously prefer the largest known balance.
var rotation = DateTime.UtcNow.DayOfYear;
metered = metered.OrderByDescending(x => x.Remaining ?? double.MaxValue)
    .ThenBy(x => (StableHash(Name(x.Provider)) + rotation) % 997).ToList();

// Some providers are explicitly expected to contribute to the cached catalog even when
// free sources have already reached the overall target. This makes UI source overrides useful.
foreach (var candidate in metered.Where(x => Number(x.Provider, "minimumCallsPerRun", 0) > 0))
{
    var requiredCalls = Math.Min(
        Number(candidate.Provider, "minimumCallsPerRun", 0),
        Math.Min(candidate.CallsLeft, searches.Count));
    foreach (var search in searches.Take(requiredCalls))
    {
        if (!HasBudget(candidate)) break;
        try
        {
            var found = await ReadMetered(candidate.Provider, search);
            jobs.AddRange(found);
            candidate.CallsLeft--;
            if (candidate.Remaining is not null)
                candidate.Remaining = Math.Max(0, candidate.Remaining.Value -
                    Number(candidate.Provider, "estimatedCreditsPerSearch", 1));
            reports.Add(new(Name(candidate.Provider), "used", candidate.Remaining, found.Count,
                "Required provider contribution for source override"));
        }
        catch (Exception ex)
        {
            candidate.CallsLeft = 0;
            reports.Add(new(Name(candidate.Provider), "failed", candidate.Remaining, 0, Short(ex.Message)));
            break;
        }
    }
}

var searchQueue = new Queue<Search>(searches);
while (jobs.Count < target && searchQueue.Count > 0 && metered.Any(x => x.CallsLeft > 0 && HasBudget(x)))
{
    var search = searchQueue.Dequeue();
    var candidate = metered.Where(x => x.CallsLeft > 0 && HasBudget(x))
        .OrderByDescending(x => x.Remaining ?? double.MaxValue)
        .ThenBy(x => (StableHash(Name(x.Provider)) + rotation) % 997).First();
    try
    {
        var found = await ReadMetered(candidate.Provider, search);
        jobs.AddRange(found);
        candidate.CallsLeft--;
        if (candidate.Remaining is not null)
            candidate.Remaining = Math.Max(0, candidate.Remaining.Value -
                Number(candidate.Provider, "estimatedCreditsPerSearch", 1));
        reports.Add(new(Name(candidate.Provider), "used", candidate.Remaining, found.Count, "Automatic quota-aware selection"));
    }
    catch (Exception ex)
    {
        candidate.CallsLeft = 0;
        reports.Add(new(Name(candidate.Provider), "failed", candidate.Remaining, 0, Short(ex.Message)));
    }
}

if (jobs.Count < target)
{
    foreach (var fallback in Enabled(root, "publicWebFallbacks").Where(x => !HasPlaceholderEndpoint(x)))
    {
        try
        {
            var found = await ReadFallback(fallback);
            jobs.AddRange(Filter(found));
            reports.Add(new(Name(fallback), "fallback", null, found.Count, "Permitted .NET HTTP fallback"));
        }
        catch (Exception ex)
        {
            reports.Add(new(Name(fallback), "failed", null, 0, Short(ex.Message)));
        }
        if (jobs.Count >= target) break;
    }
}

var unique = jobs.Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Description))
    .GroupBy(x => !string.IsNullOrWhiteSpace(x.Url) ? x.Url : $"{x.Title}|{x.Company}|{x.Location}",
        StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
if (unique.Count == 0 && File.Exists(outputFile))
{
    try
    {
        var previous = JsonNode.Parse(await File.ReadAllTextAsync(outputFile));
        if ((previous?["jobs"] as JsonArray)?.Count > 0)
        {
            Console.WriteLine("No sources produced matching jobs; retaining the last successful catalog.");
            return;
        }
    }
    catch (JsonException) { }
}
Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
var providers = unique.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var output = new
{
    generatedAt = DateTimeOffset.UtcNow,
    provider = string.Join(", ", providers),
    providers,
    keywords,
    locations,
    searchCount = searches.Count,
    routing = reports,
    jobs = unique
};
await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(output,
    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Wrote {unique.Count} jobs from {string.Join(", ", providers)}.");

bool HasBudget(Metered item)
{
    if (item.Remaining is null) return true;
    var minimum = Number(item.Provider, "minimumRemainingCredits", 1);
    var cost = Number(item.Provider, "estimatedCreditsPerSearch", 1);
    return item.Remaining >= Math.Max(minimum, cost);
}

async Task<double?> Remaining(JsonObject provider)
{
    var endpoint = Replace(Text(provider, "balanceEndpoint"), provider, null);
    if (endpoint.Length == 0) return null;
    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
    Authenticate(request, provider);
    var json = await SendJson(request, Name(provider));
    var direct = DecimalAt(json, Text(provider, "balancePath"));
    if (direct is not null) return direct;
    var allowance = DecimalAt(json, Text(provider, "allowancePath"));
    var used = DecimalAt(json, Text(provider, "usedPath"));
    return allowance is not null && used is not null ? Math.Max(0, allowance.Value - used.Value) : null;
}

async Task<List<Job>> ReadFreeSource(JsonObject source)
{
    var type = Text(source, "type").ToLowerInvariant();
    if (type == "rss")
        return ReadRss(await http.GetStringAsync(Text(source, "endpoint")), Name(source));
    var json = await GetJson(Text(source, "endpoint"), Name(source));
    IEnumerable<JsonNode?> nodes = type switch
    {
        "remoteok" => json is JsonArray remoteArray
            ? remoteArray.SkipWhile(node => node is JsonObject item && item["legal"] is not null)
            : (At(json, "jobs") as JsonArray ?? []),
        "remotive" => At(json, "jobs")?.AsArray() ?? [],
        _ => At(json, Text(source, "itemsPath"))?.AsArray() ?? []
    };
    return nodes.OfType<JsonObject>().Select(x => Normalize(x, Name(source))).ToList();
}

static List<Job> ReadRss(string xml, string source)
{
    var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
    return document.Descendants().Where(x => x.Name.LocalName is "item" or "entry").Select(item =>
    {
        string Element(params string[] names) => item.Elements()
            .FirstOrDefault(x => names.Contains(x.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value ?? "";
        var rawTitle = Element("title");
        var separator = rawTitle.IndexOf(':');
        var company = separator > 0 ? rawTitle[..separator].Trim() : "";
        var title = separator > 0 ? rawTitle[(separator + 1)..].Trim() : rawTitle;
        var linkElement = item.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase));
        var link = linkElement?.Attribute("href")?.Value ?? linkElement?.Value ?? Element("guid");
        var region = Element("region", "location");
        return new Job(
            Element("guid", "id"),
            WebUtility.HtmlDecode(title),
            WebUtility.HtmlDecode(company),
            string.IsNullOrWhiteSpace(region) ? "Remote" : $"Remote · {WebUtility.HtmlDecode(region)}",
            Strip(Element("description", "summary", "content", "encoded")),
            link.Trim(),
            Element("pubDate", "published", "updated"),
            source);
    }).ToList();
}

async Task<List<Job>> ReadMetered(JsonObject provider, Search search)
{
    var type = Text(provider, "type").ToLowerInvariant();
    if (type == "theirstack")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.theirstack.com/v1/jobs/search");
        Authenticate(request, provider);
        request.Content = JsonContent.Create(new
        {
            job_title_or = new[] { search.Keyword },
            job_location_pattern_or = search.Location.Length > 0 ? new[] { search.Location } : null,
            posted_at_max_age_days = days,
            limit,
            page = 0
        });
        var json = await SendJson(request, Name(provider));
        return Nodes(json, "data").Select(x => Normalize(x, Name(provider))).ToList();
    }
    if (type == "serpapi")
    {
        var query = string.Join(' ', new[] { search.Keyword, Text(catalog, "experienceLevel") }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var location = search.Location.Equals("Remote", StringComparison.OrdinalIgnoreCase) ||
                       search.Location.Equals("Worldwide", StringComparison.OrdinalIgnoreCase)
            ? ""
            : search.Location;
        var endpoint = "https://serpapi.com/search.json?engine=google_jobs" +
            $"&q={Uri.EscapeDataString(query)}&location={Uri.EscapeDataString(location)}" +
            SerpApiDateFilter(days) +
            (search.Location.Equals("Remote", StringComparison.OrdinalIgnoreCase) ? "&ltype=1" : "") +
            $"&api_key={Uri.EscapeDataString(Text(provider, "apiKey"))}";
        return Nodes(await GetJson(endpoint, Name(provider)), "jobs_results")
            .Select(x => Normalize(x, Name(provider))).ToList();
    }
    var url = Replace(Text(provider, "endpoint"), provider, search);
    using var generic = new HttpRequestMessage(new HttpMethod(Text(provider, "method", "GET")), url);
    Authenticate(generic, provider);
    if (generic.Method != HttpMethod.Get)
        generic.Content = new StringContent(ReplaceBodyTokens(provider["body"]?.ToJsonString() ?? "{}", search),
            Encoding.UTF8, "application/json");
    return Nodes(await SendJson(generic, Name(provider)), Text(provider, "itemsPath"))
        .Select(x => Normalize(x, Name(provider))).ToList();
}

async Task<List<Job>> ReadFallback(JsonObject fallback)
{
    var endpoint = Text(fallback, "endpoint");
    if (endpoint.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase))
        return [];
    if (Text(fallback, "type").Equals("jsonld", StringComparison.OrdinalIgnoreCase))
    {
        var html = await http.GetStringAsync(endpoint);
        var found = new List<Job>();
        foreach (Match match in Regex.Matches(html,
            """<script[^>]+type=["']application/ld\+json["'][^>]*>(.*?)</script>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            try
            {
                var node = JsonNode.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
                foreach (var item in JobPostingNodes(node))
                    found.Add(Normalize(item, Name(fallback)));
            }
            catch (JsonException) { }
        }
        return found;
    }
    var json = await GetJson(endpoint, Name(fallback));
    return Nodes(json, Text(fallback, "itemsPath")).Select(x => Normalize(x, Name(fallback))).ToList();
}

List<Job> Filter(IEnumerable<Job> values)
{
    var candidates = values.ToList();
    var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
    return candidates.Where(job =>
        searches.Count == 0 || searches.Any(search =>
            MatchesRole(job.Title + " " + RelevantDescription(Strip(job.Description)), search.Keyword) &&
            (search.Location.Length == 0 || MatchesLocation(job.Location, search.Location))))
        .Where(job => !DateTimeOffset.TryParse(job.PostedAt, out var date) || date >= cutoff)
        .Take(Math.Max(limit * Math.Max(1, searches.Count), target)).ToList();
}

Job Normalize(JsonObject item, string source)
{
    string Pick(params string[] paths) => paths.Select(x => ValueAt(item, x))
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    var location = Pick("location", "job_location", "formatted_location", "candidate_required_location",
        "location.name", "job_location_pattern");
    return new(
        Pick("job_id", "id", "htidocid", "slug"),
        WebUtility.HtmlDecode(Pick("title", "job_title", "position", "name")),
        WebUtility.HtmlDecode(Pick("company_name", "companyName", "company", "employer_name", "organization", "hiringOrganization.name")),
        WebUtility.HtmlDecode(location),
        Strip(Pick("description", "jobDescription", "descriptionHtml", "job_description", "description_text", "content", "snippet", "keySkills")),
        Pick("job_url", "jobUrl", "url", "applyUrl", "apply_link", "share_link", "link", "absolute_url"),
        Pick("posted_at", "postedAt", "posted_date", "detected_extensions.posted_at", "date_posted", "publication_date", "datePosted"),
        source);
}

void Authenticate(HttpRequestMessage request, JsonObject provider)
{
    var key = Text(provider, "apiKey");
    switch (Text(provider, "authentication").ToLowerInvariant())
    {
        case "rapidapi":
            request.Headers.TryAddWithoutValidation("X-RapidAPI-Key", key);
            request.Headers.TryAddWithoutValidation("X-RapidAPI-Host", Text(provider, "host"));
            break;
        case "api-key":
            request.Headers.TryAddWithoutValidation(Text(provider, "apiKeyHeader", "X-API-Key"), key);
            break;
        default:
            if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            break;
    }
}

async Task<JsonNode> GetJson(string url, string name)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    return await SendJson(request, name);
}

async Task<JsonNode> SendJson(HttpRequestMessage request, string name)
{
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"{name} returned {(int)response.StatusCode}: {Short(text)}");
    return JsonNode.Parse(text) ?? new JsonObject();
}

static IEnumerable<JsonObject> Nodes(JsonNode rootNode, string route) =>
    (At(rootNode, route) as JsonArray ?? []).OfType<JsonObject>();
static JsonNode? At(JsonNode? node, string route)
{
    if (string.IsNullOrWhiteSpace(route)) return node;
    var current = node;
    foreach (var part in route.Split('.'))
    {
        if (current is not JsonObject item || !item.TryGetPropertyValue(part, out current))
            return null;
    }
    return current;
}
static string ValueAt(JsonNode node, string route)
{
    var value = At(node, route);
    if (value is JsonArray array) return string.Join(", ", array.Select(x => x?.ToString()).Where(x => x is not null));
    return value?.ToString() ?? "";
}
static double? DecimalAt(JsonNode node, string route) =>
    double.TryParse(ValueAt(node, route), out var value) ? value : null;
static IEnumerable<JsonObject> Enabled(JsonObject rootObject, string property) =>
    All(rootObject, property).Where(x => Bool(x, "enabled", true));
static IEnumerable<JsonObject> All(JsonObject rootObject, string property) =>
    rootObject[property]?.AsArray().OfType<JsonObject>() ?? [];
static bool Bool(JsonObject node, string property, bool fallback) =>
    node[property]?.GetValue<bool?>() ?? fallback;
static int Number(JsonObject node, string property, int fallback) =>
    node[property]?.GetValue<int?>() ?? fallback;
static string Text(JsonObject node, string property, string fallback = "") =>
    node[property]?.GetValue<string>() ?? fallback;
static List<string> StringArray(JsonObject node, string property) =>
    node[property]?.AsArray().Select(x => x?.GetValue<string>()?.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
static string Name(JsonObject node) => Text(node, "name", Text(node, "type", "Provider"));
static bool HasPlaceholderConfiguration(JsonObject provider)
{
    if (!Text(provider, "type").Equals("generic", StringComparison.OrdinalIgnoreCase)) return false;
    return HasPlaceholderEndpoint(provider);
}
static bool HasPlaceholderEndpoint(JsonObject item) =>
    new[] { Text(item, "endpoint"), Text(item, "host") }
        .Any(value => value.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase) ||
                      value.Contains("company.example", StringComparison.OrdinalIgnoreCase));
static string StatusCode(Exception exception)
{
    var match = Regex.Match(exception.Message, @"\b(4\d\d|5\d\d)\b");
    return match.Success ? $"HTTP {match.Groups[1].Value}" : "provider response unavailable";
}
string Replace(string value, JsonObject provider, Search? search) =>
    ReplaceTokens(value.Replace("{apiKey}", Uri.EscapeDataString(Text(provider, "apiKey"))), search);
string ReplaceTokens(string value, Search? search) => value
    .Replace("{keyword}", Uri.EscapeDataString(search?.Keyword ?? ""))
    .Replace("{location}", Uri.EscapeDataString(search?.Location ?? ""))
    .Replace("{experience}", "")
    .Replace("{page}", "1")
    .Replace("{timeFrame}", RapidApiTimeFrame(days))
    .Replace("{days}", days.ToString()).Replace("{limit}", limit.ToString());
string ReplaceBodyTokens(string value, Search? search) => value
    .Replace("\"{days}\"", days.ToString())
    .Replace("\"{limit}\"", limit.ToString())
    .Replace("{keyword}", JsonString(search?.Keyword ?? ""))
    .Replace("{location}", JsonString(search?.Location ?? ""))
    .Replace("{timeFrame}", RapidApiTimeFrame(days))
    .Replace("{days}", days.ToString()).Replace("{limit}", limit.ToString());
static string RapidApiTimeFrame(int ageDays) =>
    ageDays <= 1 ? "24h" :
    ageDays <= 7 ? "7d" :
    "6m";
static string JsonString(string value)
{
    var serialized = JsonSerializer.Serialize(value);
    return serialized.Length >= 2 ? serialized[1..^1] : "";
}
static string SerpApiDateFilter(int ageDays) =>
    ageDays <= 1 ? "&chips=date_posted%3Atoday" :
    ageDays <= 3 ? "&chips=date_posted%3A3days" :
    ageDays <= 7 ? "&chips=date_posted%3Aweek" :
    ageDays <= 30 ? "&chips=date_posted%3Amonth" : "";
static bool Contains(string value, string expected) =>
    value.Contains(expected, StringComparison.OrdinalIgnoreCase);
static bool MatchesRole(string value, string role)
{
    if (string.IsNullOrWhiteSpace(role)) return true;
    if (Contains(value, role)) return true;
    var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "developer", "engineer", "specialist", "professional", "senior", "junior", "lead" };
    var tokens = Regex.Matches(role.ToLowerInvariant(), @"[a-z0-9+#.]+")
        .Select(x => x.Value).Where(x => x.Length > 1 && !generic.Contains(x)).Distinct().ToArray();
    return tokens.Length == 0
        ? role.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(token => Contains(value, token))
        : tokens.All(token => Contains(value, token));
}
static bool MatchesLocation(string jobLocation, string requestedLocation)
{
    if (Contains(jobLocation, "worldwide") || Contains(jobLocation, "anywhere"))
        return true;
    if (requestedLocation.Equals("Worldwide", StringComparison.OrdinalIgnoreCase))
        return true;
    if (requestedLocation.Equals("Remote", StringComparison.OrdinalIgnoreCase))
        return Contains(jobLocation, "remote") || Contains(jobLocation, "worldwide") || Contains(jobLocation, "anywhere");
    if (requestedLocation.Equals("Worldwide", StringComparison.OrdinalIgnoreCase))
        return Contains(jobLocation, "worldwide") || Contains(jobLocation, "anywhere");
    return Contains(jobLocation, requestedLocation);
}
static string RelevantDescription(string value)
{
    var text = value.Replace("\r\n", "\n").Replace('\r', '\n');
    var start = Regex.Match(text, @"(?im)^\s*(about the job|about this role|the role|what you(?:'|’)ll do)\s*:?\s*$");
    if (start.Success) text = text[start.Index..];
    var end = Regex.Match(text, @"(?im)^\s*(travel|what we offer|compensation|salary|benefits|equal opportunity|to apply)\s*:?\s*$");
    if (end.Success) text = text[..end.Index];
    return Regex.Replace(text, @"\s+", " ").Trim();
}
static string Strip(string value) => WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "))
    .Replace("\u00a0", " ").Trim();
static string Short(string value) => value.Length <= 300 ? value : value[..300];
static int StableHash(string value)
{
    unchecked { var hash = 17; foreach (var c in value) hash = hash * 31 + c; return hash & int.MaxValue; }
}
static IEnumerable<JsonObject> JobPostingNodes(JsonNode? node)
{
    if (node is JsonObject obj)
    {
        if (ValueAt(obj, "@type").Equals("JobPosting", StringComparison.OrdinalIgnoreCase)) yield return obj;
        foreach (var child in obj.Select(x => x.Value))
            foreach (var job in JobPostingNodes(child)) yield return job;
    }
    else if (node is JsonArray array)
        foreach (var child in array)
            foreach (var job in JobPostingNodes(child)) yield return job;
}

sealed record Search(string Keyword, string Location);
sealed record Job(string Id, string Title, string Company, string Location, string Description,
    string Url, string PostedAt, string Source);
sealed record ProviderReport(string Name, string Status, double? RemainingCredits, int JobsReturned, string Note);
sealed class Metered(JsonObject provider, double? remaining, int callsLeft)
{
    public JsonObject Provider { get; } = provider;
    public double? Remaining { get; set; } = remaining;
    public int CallsLeft { get; set; } = callsLeft;
}
