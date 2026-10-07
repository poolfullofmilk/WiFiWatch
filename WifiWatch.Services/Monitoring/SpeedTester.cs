using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using WifiWatch.Data.Models;

namespace WifiWatch.Services.Monitoring;

public static class SpeedTester
{
    // Cloudflare's Public Speed Test Endpoints, No Account Or Install
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int UploadChunkBytes = 10_000_000;

    // Timing
    private static readonly TimeSpan s_phaseLength = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan s_pingInterval = TimeSpan.FromMilliseconds(250);
    private static readonly IPAddress s_pingTarget = IPAddress.Parse("1.1.1.1");

    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    static SpeedTester() => Debug.Assert(Grade(3) == "A+" && Grade(45) == "B" && Grade(500) == "F");

    public static async Task<SpeedTest> RunAsync(string link)
    {
        var idlePing = Median(await PingWhileAsync(Task.Delay(TimeSpan.FromSeconds(2))));

        using var stopDownloadPings = new CancellationTokenSource();
        var downloadPings = PingUntilAsync(stopDownloadPings.Token);
        var downloadMbps = await MeasureAsync(DownloadOnceAsync);
        await stopDownloadPings.CancelAsync();

        using var stopUploadPings = new CancellationTokenSource();
        var uploadPings = PingUntilAsync(stopUploadPings.Token);
        var uploadMbps = await MeasureAsync(UploadOnceAsync);
        await stopUploadPings.CancelAsync();

        var downloadPing = Median(await downloadPings);
        var uploadPing = Median(await uploadPings);
        var addedLatency = Math.Max(downloadPing - idlePing ?? 0, uploadPing - idlePing ?? 0);

        return new SpeedTest
        {
            TestedAtUtc = DateTime.UtcNow,
            Link = link,
            DownloadMbps = Math.Round(downloadMbps, 1),
            UploadMbps = Math.Round(uploadMbps, 1),
            IdlePingMilliseconds = idlePing,
            DownloadPingMilliseconds = downloadPing,
            UploadPingMilliseconds = uploadPing,
            Grade = Grade(addedLatency),
        };
    }

    public static string Grade(double addedMilliseconds)
    {
        // Bufferbloat Grades As Waveform Defines Them
        return addedMilliseconds switch
        {
            < 5 => "A+",
            < 30 => "A",
            < 60 => "B",
            < 200 => "C",
            < 400 => "D",
            _ => "F",
        };
    }

    private static async Task<double> MeasureAsync(Func<Task<long>> transferOnceAsync)
    {
        // Fixed Time Transfers, One Burst Ends Too Fast On Fiber
        var stopwatch = Stopwatch.StartNew();
        long totalBytes = 0;
        while (stopwatch.Elapsed < s_phaseLength)
        {
            totalBytes += await transferOnceAsync();
        }

        return totalBytes * 8 / stopwatch.Elapsed.TotalSeconds / 1_000_000;
    }

    private static async Task<long> DownloadOnceAsync()
    {
        using var response = await s_httpClient.GetAsync(
            DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead
        );
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long totalBytes = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            totalBytes += read;
        }

        return totalBytes;
    }

    private static async Task<long> UploadOnceAsync()
    {
        using var content = new ByteArrayContent(new byte[UploadChunkBytes]);
        using var response = await s_httpClient.PostAsync(UploadUrl, content);
        return UploadChunkBytes;
    }

    private static async Task<List<double>> PingWhileAsync(Task duration)
    {
        using var stop = new CancellationTokenSource();
        var pings = PingUntilAsync(stop.Token);
        await duration;
        await stop.CancelAsync();
        return await pings;
    }

    private static async Task<List<double>> PingUntilAsync(CancellationToken token)
    {
        using var ping = new Ping();
        List<double> roundTrips = [];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var reply = await ping.SendPingAsync(s_pingTarget, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(reply.RoundtripTime);
                }

                await Task.Delay(s_pingInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (PingException)
            {
                // A Lost Ping Under Load Is Part Of The Result
            }
        }

        return roundTrips;
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
            return null;

        var sorted = values.Order().ToList();
        return Math.Round(sorted[sorted.Count / 2], 1);
    }
}
