using System.Net.Http;

namespace WifiWatch.Services.Integration;

public static class UpdateChecker
{
    public const string LatestReleaseUrl =
        "https://github.com/poolfullofmilk/WifiWatch/releases/latest";

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
            var tag = location[(location.LastIndexOf('/') + 1)..].TrimStart('v', 'V');

            return Version.TryParse(tag, out var latest) ? latest : null;
        }
        catch
        {
            // Unreachable Means Unknown, Never Up To Date
            return null;
        }
    }
}
