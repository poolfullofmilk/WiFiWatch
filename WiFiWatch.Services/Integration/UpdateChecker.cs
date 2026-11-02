using System.Net.Http;
using System.Text.Json.Nodes;
using WiFiWatch.Data.Helpers;

namespace WiFiWatch.Services.Integration;

public static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/poolfullofmilk/WiFiWatch";
    public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/poolfullofmilk/WiFiWatch/";

    private static readonly HttpClient s_httpClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // The Tag As GitHub Names It, So Notes And Downloads Never Rebuild It
    private static string? s_latestTag;

    public static async Task<Version?> CheckAsync()
    {
        try
        {
            // The Latest Release Url Redirects To The Tagged Release
            using var response = await s_httpClient.GetAsync(
                LatestReleaseUrl,
                HttpCompletionOption.ResponseHeadersRead
            );
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            var tag = location[(location.LastIndexOf('/') + 1)..];
            var latest = AppInfo.ParseTag(tag);
            if (latest is not null)
                s_latestTag = tag;
            return latest;
        }
        catch
        {
            // Unreachable Means Unknown, Never Up To Date
            return null;
        }
    }

    // The Bridge Tag v2.0 Shows As 2, So Only The Checked Tag Names It Right
    internal static string TagOf(Version version) =>
        s_latestTag is { } tag && AppInfo.ParseTag(tag) == version
            ? tag
            : "v" + AppInfo.ShortText(version);

    public static async Task<List<string>> NotesAsync(Version version)
    {
        var tag = TagOf(version);
        try
        {
            // A Workflow Build Lists Every Commit Since Itself, Newest First Like The Body
            var lines = AppInfo.Commit is { } commit
                ? (await GetJsonAsync($"compare/{commit}...{tag}"))
                    ?["commits"]?.AsArray()
                    .Reverse()
                    .Select(entry =>
                        entry?["commit"]?["message"]?.GetValue<string>().Split('\n')[0]
                    )
                : (await GetJsonAsync("releases/tags/" + tag))
                    ?["body"]?.GetValue<string>()
                    .Split('\n');

            return
            [
                .. (lines ?? [])
                    .OfType<string>()
                    .Select(line => line.Trim().TrimStart('-', '*').Trim())
                    .Where(line => line.Length > 0)
                    .Distinct(),
            ];
        }
        catch
        {
            // Notes Are Extra, The Update Works Without Them
            return [];
        }
    }

    internal static async Task<JsonNode?> GetJsonAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl + path);

        // GitHub's API Refuses Requests Without A User Agent
        request.Headers.UserAgent.ParseAdd("WiFiWatch");
        using var response = await s_httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }
}
