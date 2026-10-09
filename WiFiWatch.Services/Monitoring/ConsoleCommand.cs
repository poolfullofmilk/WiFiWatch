using System.Diagnostics;

namespace WiFiWatch.Services.Monitoring;

public static class ConsoleCommand
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

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
        // Close The Stream Now, Not At The Finalizer
        using var reader = process.StandardOutput;

        // A Hung Command Must Not Freeze The Monitor Loop
        using var timeout = new CancellationTokenSource(s_timeout);
        try
        {
            var output = await reader.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return output;
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException();
        }
    }
}
