using System.Diagnostics;

namespace WifiWatch.Services.Monitoring;

public static class ConsoleCommand
{
    public static async Task<string> RunAsync(string fileName, string arguments)
    {
        using var process = Process.Start(
            new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            }
        )!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }
}
