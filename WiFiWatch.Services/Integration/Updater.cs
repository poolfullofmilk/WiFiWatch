using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace WiFiWatch.Services.Integration;

public static class Updater
{
    // Releases Always Carry One Exe Named After Their Version
    private const string ExecutablePrefix = "WiFiWatch_v";
    private const string AssetUrlFormat =
        "https://github.com/poolfullofmilk/WiFiWatch/releases/download/v{0}/WiFiWatch_v{0}.exe";
    private const string AfterUpdateArgument = "--after-update";

    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly TimeSpan s_previousExitWait = TimeSpan.FromSeconds(15);

    public static bool CanInstall =>
        Path.GetFileName(Environment.ProcessPath)
            ?.StartsWith(ExecutablePrefix, StringComparison.OrdinalIgnoreCase) == true;

    public static async Task<string> DownloadAsync(Version version)
    {
        var versionText = version.ToString(2);
        var targetPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath)!,
            $"{ExecutablePrefix}{versionText}.exe"
        );
        var partialPath = targetPath + ".download";

        using var response = await s_httpClient.GetAsync(
            string.Format(AssetUrlFormat, versionText),
            HttpCompletionOption.ResponseHeadersRead
        );
        response.EnsureSuccessStatusCode();
        await using (var file = File.Create(partialPath))
        {
            await response.Content.CopyToAsync(file);
        }

        // Only A Complete Download Gets The Real Name
        File.Move(partialPath, targetPath, overwrite: true);
        return targetPath;
    }

    public static void Launch(string executablePath) =>
        Process.Start(
            new ProcessStartInfo(executablePath, $"{AfterUpdateArgument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
            }
        );

    public static void FinishPreviousVersion(string[] arguments)
    {
        var index = Array.IndexOf(arguments, AfterUpdateArgument);
        if (
            index < 0
            || index + 1 >= arguments.Length
            || !int.TryParse(arguments[index + 1], out var processId)
        )
            return;

        try
        {
            using var previous = Process.GetProcessById(processId);
            previous.WaitForExit(s_previousExitWait);
        }
        catch (ArgumentException)
        {
            // Already Gone
        }

        // Old Exes Beside This One Are Replaced Builds, Not Data
        foreach (
            var oldPath in Directory.EnumerateFiles(
                Path.GetDirectoryName(Environment.ProcessPath)!,
                $"{ExecutablePrefix}*.exe"
            )
        )
        {
            if (string.Equals(oldPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(oldPath);
            }
            catch
            {
                // A Locked Old Exe Is Left For Next Time
            }
        }
    }
}
