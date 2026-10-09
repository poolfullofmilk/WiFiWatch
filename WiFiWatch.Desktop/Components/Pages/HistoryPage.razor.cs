using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WiFiWatch.Data;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop.Components.Shared.Common;
using WiFiWatch.Desktop.Theming;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;
using Color = MudBlazor.Color;

namespace WiFiWatch.Desktop.Components.Pages;

public partial class HistoryPage
{
    // Ranges And Chart Shape
    private const int HourlyDayLimit = 3;
    private const int MaximumTicks = 12;
    private const int BusyAirtimePercent = 50;

    private static readonly (string Label, int DayCount)[] s_presets =
    [
        ("Today", 1),
        ("7 Days", 7),
        ("30 Days", 30),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    // Chart Options
    private readonly ApexChartOptions<ChartPoint> _pingOptions = BuildPingOptions();

    // Range State
    private DateRange _range = new(DateTime.Today, DateTime.Today);
    private MudDateRangePicker? _rangePicker;
    private bool _isDaily;
    private bool _isLoading = true;
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
    private double? _airtimePercent;
    private List<NeighborReading> _neighbors = [];

    private List<ChannelBlock> ChannelBlocks =>
        Monitor.Status.Reading?.Channel < 36
            ? [new(1, 13)]
            : [.. ChannelAdvice.Blocks, new(132, 140)];

    private string EvictionText => Formatter.FormatCount(_evictionCount, "Radar Eviction");

    // A Range Spent On A Cable Has No Wi-Fi Channel
    private bool ShowsChannelPanel => _summary is not { DfsPercent: null };

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
                    _speedTests.Count == 0
                        ? "No Speed Test"
                        : Formatter.FormatCount(_speedTests.Count, "Speed Test"),
                    Color.Default
                ),
            ];

    protected override Task OnInitializedAsync() => LoadAsync();

    #region Range Methods
    private async Task ApplyPresetAsync(int dayCount)
    {
        // Presets Apply At Once, Picked Days Wait For OK
        await _rangePicker!.CloseAsync(false);
        await SelectRangeAsync(new(DateTime.Today.AddDays(1 - dayCount), DateTime.Today));
    }

    private async Task SelectRangeAsync(DateRange? range)
    {
        _range = range is { Start: not null, End: not null }
            ? range
            : new(DateTime.Today, DateTime.Today);
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

        await using var context = new WiFiDbContext();
        var hours = await SummaryWriter.LoadHoursAsync(context, startUtc, endUtc);
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
        _buckets = HealthBuckets.Build(hours, incidents, startsUtc, endUtc, DateTime.UtcNow);
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

        // Distinct Rows In SQL, Strongest Reading Per Network Here
        var scanned = await context
            .NeighborSamples.AsNoTracking()
            .Where(sample => sample.ScanUtc >= startUtc && sample.ScanUtc < endUtc)
            .Select(sample => new
            {
                sample.IsOwn,
                sample.Ssid,
                sample.Bssid,
                sample.Channel,
                sample.SignalPercent,
            })
            .Distinct()
            .ToListAsync();
        _neighbors =
        [
            .. scanned
                .Where(row => !row.IsOwn)
                .GroupBy(row => (row.Ssid, row.Bssid, row.Channel))
                .Select(network => new NeighborReading(
                    network.Key.Ssid,
                    network.Key.Bssid,
                    network.Key.Channel,
                    network.Max(row => row.SignalPercent),
                    null
                )),
        ];
        _airtimePercent = await context
            .NeighborSamples.Where(sample =>
                sample.ScanUtc >= startUtc
                && sample.ScanUtc < endUtc
                && sample.IsOwn
                && sample.ChannelUtilizationPercent != null
            )
            .AverageAsync(sample => (double?)sample.ChannelUtilizationPercent);
        var neighborsPerBlock = ChannelAdvice.CountNeighbors(
            _neighbors.Select(neighbor =>
                (neighbor.Bssid, neighbor.Channel, neighbor.SignalPercent)
            )
        );
        var hasScans = scanned.Count > 0;

        // Without Scans Every Block Looks Empty, So Advise Nothing
        _channelAdvice = hasScans
            ? ChannelAdvice.Advise(
                Monitor.Status.Reading?.Channel,
                neighborsPerBlock,
                _evictionCount
            )
            : "No Wi-Fi Scans In This Range";
        _summary = await SummaryWriter.SummarizeAsync(context, hours, startUtc, endUtc);

        // A New Key Rebuilds The Chart With The New Range
        _renderKey++;
        _isLoading = false;
    }
    #endregion

    #region Action Methods
    private static async Task ExportMinutesAsync(DateTime? day)
    {
        var (startUtc, endUtc) = CsvExport.DayRangeUtc(day);
        await using var context = new WiFiDbContext();
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
                        WiFiChannels.IsDfs(sample.Channel) ? "Yes" : "No",
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
            ChartTheme.VioletColor,
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
