using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static class SpeedTester
{
    // Fast.com's Servers, Netflix Picks Ones Inside Your Provider
    // Ponytail: Public Fast.com Token, Scrape Fast.com If It Changes
    private const string ServersUrl =
        "https://api.fast.com/netflix/speedtest/v2?https=true&token=YXNkZmFzZGxmbnNkYWZoYXNkZmhrYWxm&urlCount=5";
    private const int UploadChunkBytes = 1_000_000;
    private const int UploadChunksPerRequest = 25;
    private const int StreamCount = 8;

    // Timing
    private static readonly TimeSpan s_idleLength = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_phaseLength = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan s_rampUpLength = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_sampleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_pingInterval = TimeSpan.FromMilliseconds(250);
    private static readonly IPAddress s_pingTarget = IPAddress.Parse("1.1.1.1");

    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly byte[] s_uploadBuffer = new byte[UploadChunkBytes];

    static SpeedTester() =>
        Debug.Assert(
            Grade(3) == "A+"
                && Grade(45) == "B"
                && Grade(500) == "F"
                && NinetiethPercentile([.. Enumerable.Range(1, 10).Select(value => (double)value)])
                    == 9
                && NinetiethPercentile([]) == 0
        );

    public static async Task<SpeedTest> RunAsync(
        string link,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        progress?.Report(new(SpeedTestPhase.Ping, 0, null, null, null));
        var servers = await LoadServersAsync(cancellationToken);
        var idlePing = Median(
            await PingWhileAsync(Task.Delay(s_idleLength, cancellationToken), cancellationToken)
        );
        progress?.Report(new(SpeedTestPhase.Ping, 1, null, idlePing, null));

        var (downloadMbps, downloadPing) = await MeasureUnderLoadAsync(
            servers,
            DownloadOnceAsync,
            report =>
                progress?.Report(
                    new(SpeedTestPhase.Download, report.Fraction, report.Mbps, idlePing, null)
                ),
            cancellationToken
        );
        var (uploadMbps, uploadPing) = await MeasureUnderLoadAsync(
            servers,
            UploadOnceAsync,
            report =>
                progress?.Report(
                    new(SpeedTestPhase.Upload, report.Fraction, report.Mbps, idlePing, downloadMbps)
                ),
            cancellationToken
        );
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

    private static async Task<List<string>> LoadServersAsync(CancellationToken token)
    {
        using var document = JsonDocument.Parse(
            await s_httpClient.GetStringAsync(ServersUrl, token)
        );
        List<string> servers =
        [
            .. document
                .RootElement.GetProperty("targets")
                .EnumerateArray()
                .Select(target => target.GetProperty("url").GetString() ?? string.Empty),
        ];
        return servers.Count > 0
            ? servers
            : throw new InvalidOperationException("No Speed Test Servers");
    }

    private static async Task<(double Mbps, double? Ping)> MeasureUnderLoadAsync(
        List<string> servers,
        Func<string, Action<long>, CancellationToken, Task> transferOnceAsync,
        Action<(double Fraction, double Mbps)> report,
        CancellationToken cancellationToken
    )
    {
        // Parallel Streams Spread Over The Servers, As Fast.com Does
        using var stopTransfers = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        long totalBytes = 0;
        var pings = PingUntilAsync(stopTransfers.Token);
        var transfers = Enumerable
            .Range(0, StreamCount)
            .Select(streamIndex =>
                TransferUntilStoppedAsync(
                    servers[streamIndex % servers.Count],
                    transferOnceAsync,
                    bytes => Interlocked.Add(ref totalBytes, bytes),
                    stopTransfers.Token
                )
            )
            .ToList();

        // Live Speed Per Second, Final Speed From Settled Seconds
        var stopwatch = Stopwatch.StartNew();
        Queue<(TimeSpan Elapsed, long Bytes)> recent = new();
        List<double> settledMbps = [];
        while (stopwatch.Elapsed < s_phaseLength && !transfers.Any(transfer => transfer.IsFaulted))
        {
            await Task.Delay(s_sampleInterval, cancellationToken);
            var elapsed = stopwatch.Elapsed;
            var bytes = Interlocked.Read(ref totalBytes);
            recent.Enqueue((elapsed, bytes));
            while (elapsed - recent.Peek().Elapsed > TimeSpan.FromSeconds(1))
            {
                recent.Dequeue();
            }

            var (oldestElapsed, oldestBytes) = recent.Peek();
            var windowSeconds = Math.Max(
                (elapsed - oldestElapsed).TotalSeconds,
                s_sampleInterval.TotalSeconds
            );
            var windowMbps = ToMbps(bytes - oldestBytes, windowSeconds);
            if (oldestElapsed >= s_rampUpLength)
            {
                settledMbps.Add(windowMbps);
            }

            report((Math.Min(1, elapsed / s_phaseLength), windowMbps));
        }

        // Ninetieth Percentile Of The Settled Seconds
        var measuredMbps = NinetiethPercentile(settledMbps);
        await stopTransfers.CancelAsync();
        await Task.WhenAll(transfers);
        return (measuredMbps, Median(await pings));
    }

    private static async Task TransferUntilStoppedAsync(
        string server,
        Func<string, Action<long>, CancellationToken, Task> transferOnceAsync,
        Action<long> addBytes,
        CancellationToken stopToken
    )
    {
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await transferOnceAsync(server, addBytes, stopToken);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Stopping Mid Transfer Is How Every Stream Ends
        }
    }

    private static async Task DownloadOnceAsync(
        string server,
        Action<long> addBytes,
        CancellationToken token
    )
    {
        using var response = await s_httpClient.GetAsync(
            server,
            HttpCompletionOption.ResponseHeadersRead,
            token
        );
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            addBytes(read);
        }
    }

    private static async Task UploadOnceAsync(
        string server,
        Action<long> addBytes,
        CancellationToken token
    )
    {
        using var content = new CountingContent(addBytes);
        using var response = await s_httpClient.PostAsync(server, content, token);
        response.EnsureSuccessStatusCode();
    }

    private static double NinetiethPercentile(List<double> values)
    {
        // Nearest Rank, So A Short Dip Or Spike Never Decides
        return values.Count == 0
            ? 0
            : values.Order().ElementAt((int)Math.Ceiling(values.Count * 0.9) - 1);
    }

    private static double ToMbps(long bytes, double seconds) =>
        seconds <= 0 ? 0 : bytes * 8 / seconds / 1_000_000;

    private static async Task<List<double>> PingWhileAsync(Task duration, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
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

    private sealed class CountingContent(Action<long> addBytes) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        )
        {
            // Count Per Chunk So Live Progress Moves Smoothly
            for (var chunkIndex = 0; chunkIndex < UploadChunksPerRequest; chunkIndex++)
            {
                await stream.WriteAsync(s_uploadBuffer);
                addBytes(s_uploadBuffer.Length);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = (long)UploadChunkBytes * UploadChunksPerRequest;
            return true;
        }
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
            return null;

        var sorted = values.Order().ToList();
        return Math.Round(sorted[sorted.Count / 2], 1);
    }
}
