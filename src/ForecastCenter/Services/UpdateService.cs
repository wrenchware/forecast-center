using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForecastCenter.Services;

public sealed record UpdateStatus(
    string InstalledVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseNotes,
    Uri? ReleaseUri,
    DateTimeOffset CheckedAt,
    bool UpdateAvailable,
    bool IsStale = false);

public sealed class UpdateService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private UpdateCache _cache;

    public UpdateService(HttpClient? http = null, string? cachePath = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);
        _cachePath = cachePath ?? Path.Combine(AppIdentity.DataRoot, "update-status.json");
        _cache = LoadCache();
    }

    public async Task<UpdateStatus> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var installed = InstalledVersion();
        if (!force && _cache.Latest is not null && DateTimeOffset.UtcNow - _cache.CheckedAt < CheckInterval)
            return CreateStatus(installed, _cache, false, false);

        try
        {
            var releases = await _http.GetFromJsonAsync<List<GitHubRelease>>(
                "https://api.github.com/repos/wrenchware/forecast-center/releases?per_page=10",
                cancellationToken) ?? [];
            var candidates = releases.Where(release => !release.Draft && ParseVersion(release.TagName) is not null).ToList();
            var latest = candidates.Where(release => !release.Prerelease)
                .MaxBy(release => ParseVersion(release.TagName))
                ?? candidates.MaxBy(release => ParseVersion(release.TagName));
            _cache = _cache with { CheckedAt = DateTimeOffset.UtcNow, Latest = latest };
            await SaveAsync();
            return CreateStatus(installed, _cache, force, false);
        }
        catch when (_cache.Latest is not null)
        {
            return CreateStatus(installed, _cache, force, true);
        }
    }

    public async Task RemindLaterAsync()
    {
        _cache = _cache with { RemindAfter = DateTimeOffset.UtcNow.AddDays(1) };
        await SaveAsync();
    }

    public async Task SkipVersionAsync(string version)
    {
        _cache = _cache with { SkippedVersion = _cache.Latest?.TagName ?? version, RemindAfter = null };
        await SaveAsync();
    }

    private UpdateStatus CreateStatus(Version installed, UpdateCache cache, bool ignoreReminder, bool stale)
    {
        var latestVersion = ParseVersion(cache.Latest?.TagName);
        var newer = latestVersion is not null && latestVersion > installed;
        var suppressed = !ignoreReminder && (string.Equals(cache.SkippedVersion, cache.Latest?.TagName, StringComparison.OrdinalIgnoreCase) || cache.RemindAfter > DateTimeOffset.UtcNow);
        return new(
            DisplayVersion(installed),
            latestVersion is null ? null : DisplayVersion(latestVersion),
            cache.Latest?.Name,
            cache.Latest?.Body,
            Uri.TryCreate(cache.Latest?.HtmlUrl, UriKind.Absolute, out var uri) ? uri : null,
            cache.CheckedAt,
            newer && !suppressed,
            stale);
    }

    private UpdateCache LoadCache()
    {
        try { return File.Exists(_cachePath) ? JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(_cachePath)) ?? new() : new(); }
        catch { return new(); }
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        await File.WriteAllTextAsync(_cachePath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Version InstalledVersion() => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    private static Version? ParseVersion(string? value) => Version.TryParse(value?.Trim().TrimStart('v', 'V'), out var version) ? version : null;
    private static string DisplayVersion(Version version) => $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private sealed record UpdateCache(
        DateTimeOffset CheckedAt = default,
        GitHubRelease? Latest = null,
        string? SkippedVersion = null,
        DateTimeOffset? RemindAfter = null);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
