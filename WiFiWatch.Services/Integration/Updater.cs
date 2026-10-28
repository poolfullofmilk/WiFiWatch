using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using WiFiWatch.Data.Helpers;

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

    // Off The Window's Thread, Writing And Hashing 80 MB Would Freeze It
    public static Task<string> DownloadAsync(Version version) =>
        Task.Run(() => DownloadOnPoolAsync(version));

    private static async Task<string> DownloadOnPoolAsync(Version version)
    {
        var versionText = AppInfo.ShortText(version);
        var fileName = $"{ExecutablePrefix}{versionText}.exe";
        var targetPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, fileName);
        var partialPath = targetPath + ".download";

        // GitHub Hashes Every Release Asset, A Download That Differs Never Runs
        var digest =
            (await UpdateChecker.GetJsonAsync("releases/tags/v" + versionText))
                ?["assets"]?.AsArray()
                .FirstOrDefault(asset => asset?["name"]?.GetValue<string>() == fileName)
                ?["digest"]?.GetValue<string>()
            ?? throw new InvalidDataException($"Release {versionText} Lists No Checksum");

        try
        {
            using var response = await s_httpClient.GetAsync(
                string.Format(AssetUrlFormat, versionText),
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();
            await using (var file = File.Create(partialPath))
            {
                await response.Content.CopyToAsync(file);
            }

            await using (var file = File.OpenRead(partialPath))
            {
                var actual = "sha256:" + Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
                if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum Mismatch For {versionText}");
            }

            // Only A Complete, Verified Download Gets The Real Name
            File.Move(partialPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
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
