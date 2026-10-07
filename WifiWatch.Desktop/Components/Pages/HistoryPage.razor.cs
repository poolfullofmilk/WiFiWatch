using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Components.Shared.Common;
using WifiWatch.Desktop.Theming;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;
using Color = MudBlazor.Color;

namespace WifiWatch.Desktop.Components.Pages;

public partial class HistoryPage
{
    // Ranges And Chart Shape
    private const int HourlyDayLimit = 3;
    private const int MaximumTicks = 12;

    private static readonly List<SegmentedButtonOption<int>> s_presetOptions =
    [
        new(1, "Today"),
        new(7, "7 Days"),
        new(30, "30 Days"),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    // Chart Options
    private readonly ApexChartOptions<ChartPoint> _pingOptions = BuildPingOptions();

    // Range State
    private DateRange _range = new(DateTime.Today, DateTime.Today);
    private int _presetDays = 1;
    private bool _isDaily;
    private bool _isLoading = true;
    private bool _isReporting;
    private int _renderKey;

    // Range Data
    private PeriodSummary? _summary;
    private List<HealthBucket> _buckets = [];
    private List<ChartPoint> _routerPingPoints = [];
    private List<ChartPoint> _internetPingPoints = [];
    private List<ChartPoint> _dnsPoints = [];
    private List<SpeedTest> _speedTests = [];

    // Channel State
    private string _channelAdvice = string.Empty;
    private int _evictionCount;

    private string EvictionText =>
        _evictionCount == 1 ? "1 Radar Eviction" : $"{_evictionCount} Radar Evictions";

    private List<TileView> Tiles =>
        _summary is not { MonitoredMinutes: > 0 } summary
            ?
            [
                new("Online", "-", "Not Watched", Color.Default),
                new("Problems", "-", "Warnings And Outages", Color.Default),
                new("Longest Outage", "-", "Internet Or Wi-Fi Down", Color.Default),
                new("Fastest Download", "-", "From Speed Tests", Color.Default),
            ]
            :
            [
                new(
                    "Online",
                    $"{summary.OnlinePercent:0.#}%",
                    $"Of {Formatter.FormatDuration(TimeSpan.FromMinutes(summary.MonitoredMinutes))} Watched",
                    summary.OnlinePercent < 99 ? Color.Warning : Color.Default
                ),
                new(
                    "Problems",
                    $"{summary.ProblemCount}",
                    "Warnings And Outages",
                    summary.ProblemCount > 0 ? Color.Warning : Color.Default
                ),
                new(
                    "Longest Outage",
                    summary.LongestOutage > TimeSpan.Zero
                        ? Formatter.FormatDuration(summary.LongestOutage)
                        : "None",
                    "Internet Or Wi-Fi Down",
                    summary.LongestOutage > TimeSpan.Zero ? Color.Error : Color.Default
                ),
                new(
                    "Fastest Download",
                    _speedTests.Count == 0
                        ? "-"
                        : $"{_speedTests.Max(test => test.DownloadMbps):0} Mbps",
                    _speedTests.Count switch
                    {
                        0 => "No Speed Test",
                        1 => "1 Speed Test",
                        _ => $"{_speedTests.Count} Speed Tests",
                    },
                    Color.Default
                ),
            ];

    protected override Task OnInitializedAsync() => LoadAsync();

    #region Range Methods
    private async Task ApplyPresetAsync(int dayCount)
    {
        _presetDays = dayCount;
        _range = new(DateTime.Today.AddDays(1 - dayCount), DateTime.Today);
        await LoadAsync();
    }

    private async Task SelectRangeAsync(DateRange? range)
    {
        _range = range is { Start: not null, End: not null }
            ? range
            : new(DateTime.Today, DateTime.Today);

        // A Range Ending Today Still Matches Its Preset
        var dayCount = (_range.End!.Value.Date - _range.Start!.Value.Date).Days + 1;
        _presetDays =
            _range.End.Value.Date == DateTime.Today
            && s_presetOptions.Any(option => option.Value == dayCount)
                ? dayCount
                : 0;
        await LoadAsync();
    }
    #endregion

    #region Load Methods
    private async Task LoadAsync()
    {
        _isLoading = true;

        // Short Ranges Chart Per Hour, Longer Ones Per Day
        var firstDay = _range.Start!.Value.Date;
        var dayCount = (_range.End!.Value.Date - firstDay).Days + 1;
        _isDaily = dayCount > HourlyDayLimit;
        var startUtc = firstDay.ToUniversalTime();
        var endUtc = firstDay.AddDays(dayCount).ToUniversalTime();
        var bucketCount = _isDaily ? dayCount : (int)(endUtc - startUtc).TotalHours;
        var startsUtc = HealthBuckets.Starts(firstDay, bucketCount, _isDaily);
        var labelFormat =
            _isDaily ? "dd MMM"
            : dayCount == 1 ? "HH'h'"
            : "ddd HH'h'";
        _pingOptions.Xaxis.TickAmount = Math.Min(bucketCount, MaximumTicks);

        await using var context = new WifiDbContext();
        var samples = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .ToListAsync();
        var incidents = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc < endUtc
                && (wifiEvent.EndedAtUtc == null || wifiEvent.EndedAtUtc >= startUtc)
            )
            .ToListAsync();
        _speedTests = await context
            .SpeedTests.AsNoTracking()
            .Where(test => test.TestedAtUtc >= startUtc && test.TestedAtUtc < endUtc)
            .OrderByDescending(test => test.TestedAtUtc)
            .ToListAsync();

        // Every Bucket Shows, Empty Ones Included
        _buckets = HealthBuckets.Build(samples, incidents, startsUtc, endUtc, DateTime.UtcNow);
        List<ChartPoint> Series(Func<HealthBucket, double?> selector) =>
            [
                .. _buckets.Select(bucket => new ChartPoint(
                    Formatter.FormatLocal(bucket.StartUtc, labelFormat),
                    selector(bucket) is { } average ? Math.Round((decimal)average, 1) : null
                )),
            ];
        _routerPingPoints = Series(bucket => bucket.RouterPing);
        _internetPingPoints = Series(bucket => bucket.InternetPing);
        _dnsPoints = Series(bucket => bucket.DnsPing);

        _evictionCount = incidents.Count(incident => incident.Kind == EventKind.DfsEviction);

        // Neighbors Over The Same Range, Our Own Network Left Out
        var neighbors = await context
            .NeighborSamples.AsNoTracking()
            .Where(sample => sample.ScanUtc >= startUtc && sample.ScanUtc < endUtc && !sample.IsOwn)
            .Select(sample => new
            {
                sample.Bssid,
                sample.Channel,
                sample.SignalPercent,
            })
            .ToListAsync();
        var neighborsPerBlock = ChannelAdvice.CountNeighbors(
            neighbors.Select(neighbor => (neighbor.Bssid, neighbor.Channel, neighbor.SignalPercent))
        );
        var hasScans = await context.NeighborSamples.AnyAsync(sample =>
            sample.ScanUtc >= startUtc && sample.ScanUtc < endUtc
        );

        // Without Scans Every Block Looks Empty, So Advise Nothing
        _channelAdvice = hasScans
            ? ChannelAdvice.Advise(
                Monitor.Status.Reading?.Channel,
                neighborsPerBlock,
                _evictionCount
            )
            : "No Wi-Fi Scans In This Range";
        _summary = await SummaryWriter.SummarizeAsync(startUtc, endUtc);

        // A New Key Rebuilds The Chart With The New Range
        _renderKey++;
        _isLoading = false;
    }
    #endregion

    #region Action Methods
    private async Task SaveReportAsync()
    {
        _isReporting = true;
        try
        {
            await ReportWriter.SaveAsync(_range.Start!.Value, _range.End!.Value);
            Snackbar.Add("Report Saved To Documents", Severity.Success);
        }
        catch (Exception exception)
        {
            Snackbar.Add($"Report Failed {exception.GetType().Name}", Severity.Warning);
        }
        finally
        {
            _isReporting = false;
        }
    }

    private static async Task ExportMinutesAsync(DateTime? day)
    {
        var (startUtc, endUtc) = CsvExport.DayRangeUtc(day);
        await using var context = new WifiDbContext();
        var samples = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .OrderBy(sample => sample.MinuteUtc)
            .ToListAsync();

        CsvExport.Save(
            "Minutes",
            day,
            [
                [
                    "Time",
                    "Link",
                    "Network",
                    "Band",
                    "Channel",
                    "DFS",
                    "Signal dBm",
                    "Receive Mbps",
                    "Transmit Mbps",
                    "Router Ping ms",
                    "Router Jitter ms",
                    "Router Loss %",
                    "Internet Ping ms",
                    "Internet Jitter ms",
                    "Internet Loss %",
                    "DNS ms",
                    "Reference DNS ms",
                ],
                .. samples.Select(sample =>
                    new object?[]
                    {
                        Formatter.FormatLocal(sample.MinuteUtc, "yyyy-MM-dd HH:mm"),
                        sample.Link,
                        sample.Ssid,
                        sample.Band,
                        sample.Channel,
                        WifiChannels.IsDfs(sample.Channel) ? "Yes" : "No",
                        sample.Rssi,
                        sample.ReceiveRateMbps,
                        sample.TransmitRateMbps,
                        sample.RouterPingMilliseconds,
                        sample.RouterJitterMilliseconds,
                        sample.RouterLossPercent,
                        sample.InternetPingMilliseconds,
                        sample.InternetJitterMilliseconds,
                        sample.InternetLossPercent,
                        sample.DnsMilliseconds,
                        sample.ReferenceDnsMilliseconds,
                    }
                ),
            ]
        );
    }
    #endregion

    #region Format Methods
    private static string LagUnderLoad(SpeedTest test)
    {
        // The Worse Direction Shows How Much A Busy Line Lags
        var loadedPing = Math.Max(
            test.DownloadPingMilliseconds ?? 0,
            test.UploadPingMilliseconds ?? 0
        );
        return test.IdlePingMilliseconds is { } idlePing
            ? $"+{Math.Max(0, loadedPing - idlePing):0} ms"
            : "-";
    }

    private static Color GradeColor(string grade) =>
        grade switch
        {
            "A+" or "A" => Color.Success,
            "B" or "C" => Color.Warning,
            _ => Color.Error,
        };
    #endregion

    #region Option Methods
    private static ApexChartOptions<ChartPoint> BuildPingOptions()
    {
        // Internet Leads In Blue, Router And DNS Stay Grey
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.Theme.Monochrome.Enabled = false;
        options.Colors =
        [
            ChartTheme.LightGreyColor,
            ChartTheme.AccentColor,
            ChartTheme.DarkGreyColor,
        ];
        options.Stroke = new Stroke
        {
            Curve = Curve.Smooth,
            Width = 2,
            DashArray = [0, 0, 6],
        };
        options.Markers = new Markers { Size = 0 };
        options.Xaxis = new XAxis { TickAmount = MaximumTicks, Labels = ChartTheme.FlatLabels() };
        options.Yaxis =
        [
            new YAxis
            {
                Min = 0,
                Labels = new YAxisLabels
                {
                    Formatter =
                        "function (value) { return value == null ? '' : Math.round(value) + ' ms'; }",
                },
            },
        ];
        return options;
    }
    #endregion

    private sealed record TileView(string Label, string Value, string Hint, Color Color);
}
