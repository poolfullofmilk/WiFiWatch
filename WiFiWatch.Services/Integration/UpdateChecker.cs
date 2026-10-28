using System.Net.Http;
using System.Text.Json.Nodes;
using WiFiWatch.Data.Helpers;

namespace WiFiWatch.Services.Integration;

public static class UpdateChecker
{
    public const string LatestReleaseUrl =
        "https://github.com/poolfullofmilk/WiFiWatch/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/poolfullofmilk/WiFiWatch/";

    private static readonly HttpClient s_httpClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

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
            return AppInfo.ParseTag(location[(location.LastIndexOf('/') + 1)..]);
        }
        catch
        {
            // Unreachable Means Unknown, Never Up To Date
            return null;
        }
    }

    public static async Task<List<string>> NotesAsync(Version version)
    {
        var tag = "v" + AppInfo.ShortText(version);
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
