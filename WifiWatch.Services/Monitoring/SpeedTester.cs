using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static class SpeedTester
{
    // Cloudflare's Public Speed Test Endpoints, No Account Or Install
    // Cloudflare Refuses 100 MB Without A Token
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=50000000";
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int UploadChunkBytes = 1_000_000;
    private const int UploadChunksPerRequest = 25;
    private const int StreamCount = 4;

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
        var idlePing = Median(
            await PingWhileAsync(Task.Delay(s_idleLength, cancellationToken), cancellationToken)
        );
        progress?.Report(new(SpeedTestPhase.Ping, 1, null, idlePing, null));

        var (downloadMbps, downloadPing) = await MeasureUnderLoadAsync(
            DownloadOnceAsync,
            report =>
                progress?.Report(
                    new(SpeedTestPhase.Download, report.Fraction, report.Mbps, idlePing, null)
                ),
            cancellationToken
        );
        var (uploadMbps, uploadPing) = await MeasureUnderLoadAsync(
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

    private static async Task<(double Mbps, double? Ping)> MeasureUnderLoadAsync(
        Func<Action<long>, CancellationToken, Task> transferOnceAsync,
        Action<(double Fraction, double Mbps)> report,
        CancellationToken cancellationToken
    )
    {
        // Parallel Streams Fill A Fast Line, One Stream Rarely Can
        using var stopTransfers = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        long totalBytes = 0;
        var pings = PingUntilAsync(stopTransfers.Token);
        var transfers = Enumerable
            .Range(0, StreamCount)
            .Select(_ =>
                TransferUntilStoppedAsync(
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

        // Ninetieth Percentile, As Cloudflare's Own Test Reports
        var measuredMbps = NinetiethPercentile(settledMbps);
        await stopTransfers.CancelAsync();
        await Task.WhenAll(transfers);
        return (measuredMbps, Median(await pings));
    }

    private static async Task TransferUntilStoppedAsync(
        Func<Action<long>, CancellationToken, Task> transferOnceAsync,
        Action<long> addBytes,
        CancellationToken stopToken
    )
    {
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await transferOnceAsync(addBytes, stopToken);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Stopping Mid Transfer Is How Every Stream Ends
        }
    }

    private static async Task DownloadOnceAsync(Action<long> addBytes, CancellationToken token)
    {
        using var response = await s_httpClient.GetAsync(
            DownloadUrl,
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

    private static async Task UploadOnceAsync(Action<long> addBytes, CancellationToken token)
    {
        using var content = new CountingContent(addBytes);
        using var response = await s_httpClient.PostAsync(UploadUrl, content, token);
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
