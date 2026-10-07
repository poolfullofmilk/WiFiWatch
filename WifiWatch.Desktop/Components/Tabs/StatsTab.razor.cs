using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Theming;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Tabs;

public partial class StatsTab : IDisposable
{
    // Ranges And Chart Shape
    private const int HourlyDayLimit = 3;
    private const int MaximumTicks = 12;
    private const int BusyAirtimePercent = 50;
    private const int TimelineRowHeight = 36;
    private const int TimelineMinimumHeight = 160;

    private static readonly string[] s_weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    [Parameter]
    public EventCallback<(DateTime StartUtc, DateTime EndUtc)> MinutesRequested { get; set; }

    // Chart Options
    private readonly ApexChartOptions<ChartPoint> _pingOptions = BuildLineOptions("ms", true);
    private readonly ApexChartOptions<ChartPoint> _jitterOptions = BuildLineOptions("ms", true);
    private readonly ApexChartOptions<ChartPoint> _lossOptions = BuildLineOptions("%", true);
    private readonly ApexChartOptions<ChartPoint> _evictionOptions = BuildCountOptions();
    private readonly ApexChartOptions<ChartPoint> _neighborOptions = BuildCountOptions();
    private readonly ApexChartOptions<ChartPoint> _speedOptions = BuildSpeedOptions();
    private readonly ApexChartOptions<ChartPoint> _heatmapOptions = BuildHeatmapOptions();
    private readonly ApexChartOptions<TimelineBar> _timelineOptions = BuildTimelineOptions();

    // Range State
    private DateRange _range = TodayRange();
    private MudDateRangePicker? _rangePicker;
    private TimeSpan _bucketSize = TimeSpan.FromHours(1);
    private bool _isLoading = true;
    private bool _isReporting;
    private int _renderKey;

    // Chart Data
    private List<ChartPoint> _routerPingPoints = [];
    private List<ChartPoint> _internetPingPoints = [];
    private List<ChartPoint> _dnsPoints = [];
    private List<ChartPoint> _routerJitterPoints = [];
    private List<ChartPoint> _internetJitterPoints = [];
    private List<ChartPoint> _routerLossPoints = [];
    private List<ChartPoint> _internetLossPoints = [];
    private List<ChartPoint> _evictionPoints = [];
    private List<ChartPoint> _neighborPoints = [];
    private List<ChartPoint> _downloadPoints = [];
    private List<ChartPoint> _uploadPoints = [];
    private List<TimelineBar> _timelineBars = [];
    private List<HeatmapRow> _heatmapRows = [];

    // Advice State
    private string _channelAdvice = string.Empty;
    private string _lastSpeedTest = string.Empty;
    private int _evictionCount;
    private int? _ownAirtimePercent;
    private double? _dfsPercent;

    private string BucketTitle => _bucketSize == TimeSpan.FromHours(1) ? "Per Hour" : "Per Day";

    private string TimelineHeight =>
        Math.Max(
                TimelineMinimumHeight,
                _timelineBars.Select(bar => bar.Label).Distinct().Count() * TimelineRowHeight + 80
            )
            .ToString();

    protected override void OnInitialized() => Monitor.StatusChanged += OnStatusChanged;

    private void OnStatusChanged() => InvokeAsync(StateHasChanged);

    public Task OpenAsync()
    {
        // Every Visit Starts On Today
        _range = TodayRange();
        return LoadAsync();
    }

    #region Range Methods
    private async Task SelectRangeAsync(DateRange? range)
    {
        _range = range is { Start: not null, End: not null } ? range : TodayRange();
        await LoadAsync();
    }

    private async Task ApplyPresetAsync(int dayCount)
    {
        await SelectRangeAsync(new(DateTime.Today.AddDays(1 - dayCount), DateTime.Today));
        if (_rangePicker is not null)
        {
            await _rangePicker.CloseAsync();
        }
    }

    private static DateRange TodayRange() => new(DateTime.Today, DateTime.Today);

    private async Task ShowMinutesAsync(SelectedData<ChartPoint> selection)
    {
        if (selection.DataPoint.Items.FirstOrDefault()?.StartUtc is { } startUtc)
        {
            await MinutesRequested.InvokeAsync((startUtc, startUtc + _bucketSize));
        }
    }
    #endregion

    #region Load Methods
    private async Task LoadAsync()
    {
        _isLoading = true;

        // Short Ranges Chart Per Hour, Longer Ones Per Day
        var firstDay = _range.Start!.Value.Date;
        var dayCount = (_range.End!.Value.Date - firstDay).Days + 1;
        var isHourly = dayCount <= HourlyDayLimit;
        _bucketSize = isHourly ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var startUtc = firstDay.ToUniversalTime();
        var endUtc = firstDay.AddDays(dayCount).ToUniversalTime();
        var bucketCount = (int)Math.Ceiling((endUtc - startUtc) / _bucketSize);
        var labelFormat =
            !isHourly ? "dd MMM"
            : dayCount == 1 ? "HH'h'"
            : "ddd HH'h'";
        // Day And Hour Labels Are Wide, Narrow Panels Show Fewer
        var hasWideLabels = isHourly && dayCount > 1;
        _pingOptions.Xaxis.TickAmount = Math.Min(bucketCount, MaximumTicks);
        _jitterOptions.Xaxis.TickAmount = Math.Min(bucketCount, hasWideLabels ? 6 : MaximumTicks);
        _lossOptions.Xaxis.TickAmount = _jitterOptions.Xaxis.TickAmount;
        _evictionOptions.Xaxis.TickAmount = hasWideLabels ? 4 : 6;

        await using var context = new WifiDbContext();
        var samples = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .ToListAsync();
        var incidents = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc
                && wifiEvent.OccurredAtUtc < endUtc
                && wifiEvent.Severity != EventSeverity.Info
            )
            .ToListAsync();

        // Every Bucket Shows, Empty Ones Included
        var samplesByBucket = samples.ToLookup(sample => BucketIndex(sample.MinuteUtc, startUtc));
        List<ChartPoint> Series(Func<Data.Models.MinuteSample, double?> selector) =>
            [
                .. Enumerable
                    .Range(0, bucketCount)
                    .Select(bucket => new ChartPoint(
                        Formatter.FormatLocal(startUtc + (bucket * _bucketSize), labelFormat),
                        samplesByBucket[bucket].Average(selector) is { } average
                            ? Math.Round((decimal)average, 1)
                            : null,
                        startUtc + (bucket * _bucketSize)
                    )),
            ];

        _routerPingPoints = Series(sample => sample.RouterPingMilliseconds);
        _internetPingPoints = Series(sample => sample.InternetPingMilliseconds);
        _dnsPoints = Series(sample => sample.DnsMilliseconds);
        _routerJitterPoints = Series(sample => sample.RouterJitterMilliseconds);
        _internetJitterPoints = Series(sample => sample.InternetJitterMilliseconds);
        _routerLossPoints = Series(sample => sample.RouterLossPercent);
        _internetLossPoints = Series(sample => sample.InternetLossPercent);

        var evictions = incidents
            .Where(incident => incident.Kind == EventKind.DfsEviction)
            .ToList();
        var evictionsByBucket = evictions
            .CountBy(eviction => BucketIndex(eviction.OccurredAtUtc, startUtc))
            .ToDictionary();
        _evictionPoints =
        [
            .. Enumerable
                .Range(0, bucketCount)
                .Select(bucket => new ChartPoint(
                    Formatter.FormatLocal(startUtc + (bucket * _bucketSize), labelFormat),
                    evictionsByBucket.GetValueOrDefault(bucket)
                )),
        ];
        _evictionCount = evictions.Count;

        var channelMinutes = samples.Where(sample => sample.Channel is not null).ToList();
        _dfsPercent =
            channelMinutes.Count == 0
                ? null
                : 100.0
                    * channelMinutes.Count(sample => WifiChannels.IsDfs(sample.Channel))
                    / channelMinutes.Count;

        _timelineOptions.Xaxis.Min = ToAxis(startUtc);
        _timelineOptions.Xaxis.Max = ToAxis(endUtc);
        BuildTimeline(incidents);
        BuildHeatmap(incidents);
        await LoadSpeedTestsAsync(context, startUtc, endUtc);
        await LoadNeighborsAsync(context, startUtc, endUtc);

        // A New Key Rebuilds The Charts With The New Range
        _renderKey++;
        _isLoading = false;
    }

    private void BuildTimeline(List<Data.Models.WifiEvent> incidents)
    {
        _timelineBars =
        [
            .. incidents.Select(incident =>
            {
                var endedAtUtc = incident.EndedAtUtc ?? DateTime.UtcNow;
                if (endedAtUtc - incident.OccurredAtUtc < TimeSpan.FromMinutes(1))
                {
                    endedAtUtc = incident.OccurredAtUtc.AddMinutes(1);
                }

                return new TimelineBar(
                    incident.Kind.ToLabel(),
                    ToAxis(incident.OccurredAtUtc),
                    ToAxis(endedAtUtc),
                    incident.Message,
                    incident.Severity == EventSeverity.Critical
                );
            }),
        ];
    }

    private static decimal ToAxis(DateTime utcTime)
    {
        // Local Time Passed As UTC Keeps Wall Clock Labels
        return (decimal)(utcTime.ToLocalTime() - DateTime.UnixEpoch).TotalMilliseconds;
    }

    private static void ColorBar(ListPoint<TimelineBar> point) =>
        point.FillColor = point.Items.First().IsCritical
            ? ChartTheme.ErrorColor
            : ChartTheme.WarningColor;

    private void BuildHeatmap(List<Data.Models.WifiEvent> incidents)
    {
        // ApexCharts Stacks Series Upward, So Sunday Goes First
        var counts = incidents
            .Select(incident => incident.OccurredAtUtc.ToLocalTime())
            .CountBy(localTime => (Day: ((int)localTime.DayOfWeek + 6) % 7, localTime.Hour))
            .ToDictionary();
        _heatmapRows =
        [
            .. Enumerable
                .Range(0, s_weekdays.Length)
                .Reverse()
                .Select(day => new HeatmapRow(
                    s_weekdays[day],
                    [
                        .. Enumerable
                            .Range(0, 24)
                            .Select(hour => new ChartPoint(
                                $"{hour:00}h",
                                counts.GetValueOrDefault((day, hour))
                            )),
                    ]
                )),
        ];
    }

    private async Task LoadSpeedTestsAsync(
        WifiDbContext context,
        DateTime startUtc,
        DateTime endUtc
    )
    {
        var speedTests = await context
            .SpeedTests.AsNoTracking()
            .Where(test => test.TestedAtUtc >= startUtc && test.TestedAtUtc < endUtc)
            .OrderBy(test => test.TestedAtUtc)
            .ToListAsync();
        _downloadPoints =
        [
            .. speedTests.Select(test => new ChartPoint(
                Formatter.FormatLocal(test.TestedAtUtc, "dd MMM HH:mm"),
                (decimal)test.DownloadMbps
            )),
        ];
        _uploadPoints =
        [
            .. speedTests.Select(test => new ChartPoint(
                Formatter.FormatLocal(test.TestedAtUtc, "dd MMM HH:mm"),
                (decimal)test.UploadMbps
            )),
        ];
        _lastSpeedTest = speedTests.LastOrDefault() is { } last
            ? $"Latest: Grade {last.Grade}, Ping Under Load {Formatter.FormatNumber(Math.Max(last.DownloadPingMilliseconds ?? 0, last.UploadPingMilliseconds ?? 0), "ms")} Against {Formatter.FormatNumber(last.IdlePingMilliseconds, "ms")} Idle"
            : string.Empty;
    }

    private async Task LoadNeighborsAsync(WifiDbContext context, DateTime startUtc, DateTime endUtc)
    {
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
        _ownAirtimePercent = await context
            .NeighborSamples.AsNoTracking()
            .Where(sample => sample.IsOwn && sample.ChannelUtilizationPercent != null)
            .OrderByDescending(sample => sample.ScanUtc)
            .Select(sample => sample.ChannelUtilizationPercent)
            .FirstOrDefaultAsync();
        var neighborsPerBlock = ChannelAdvice.CountNeighbors(
            neighbors.Select(neighbor => (neighbor.Bssid, neighbor.Channel, neighbor.SignalPercent))
        );
        _neighborPoints =
        [
            .. ChannelAdvice.Blocks.Select(block => new ChartPoint(
                block.Label,
                neighborsPerBlock[block]
            )),
        ];
        _channelAdvice = ChannelAdvice.Advise(
            Monitor.Status.Reading?.Channel,
            neighborsPerBlock,
            _evictionCount
        );
    }

    private int BucketIndex(DateTime utcTime, DateTime startUtc) =>
        (int)((utcTime - startUtc) / _bucketSize);
    #endregion

    #region Action Methods
    private async Task RunSpeedTestAsync()
    {
        Snackbar.Add("Speed Test Running, About 20 Seconds", Severity.Info);
        var result = await Monitor.RunSpeedTestAsync();
        if (result is null)
        {
            Snackbar.Add("Speed Test Failed", Severity.Warning);
            return;
        }

        Snackbar.Add(
            $"{result.DownloadMbps:0} Mbps Down, {result.UploadMbps:0} Mbps Up, Grade {result.Grade}",
            Severity.Success
        );
        await LoadAsync();
    }

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
    #endregion

    #region Option Methods
    private static ApexChartOptions<ChartPoint> BuildLineOptions(string unit, bool isZoomable)
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        if (isZoomable)
        {
            // Drag Or Buttons Zoom, Reset Shows The Whole Range
            options.Chart.Zoom = new Zoom
            {
                Enabled = true,
                AutoScaleYaxis = true,
                AllowMouseWheelZoom = false,
            };
            options.Chart.Toolbar = new Toolbar
            {
                Show = true,
                Tools = new Tools
                {
                    Download = false,
                    Selection = false,
                    Zoom = true,
                    Zoomin = true,
                    Zoomout = true,
                    Pan = false,
                    Reset = true,
                    Measure = false,
                },
            };
        }

        options.Stroke = new Stroke { Curve = Curve.Smooth, Width = 3 };
        options.Markers = new Markers { Size = 3 };
        options.Xaxis = new XAxis { TickAmount = MaximumTicks, Labels = ChartTheme.FlatLabels() };
        var suffix = unit == "%" ? "%" : $" {unit}";
        options.Yaxis =
        [
            new YAxis
            {
                Labels = new YAxisLabels
                {
                    Formatter =
                        $"function (value) {{ return value == null ? '' : Math.round(value) + '{suffix}'; }}",
                },
            },
        ];
        return options;
    }

    private static ApexChartOptions<ChartPoint> BuildCountOptions()
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.PlotOptions = new PlotOptions { Bar = new PlotOptionsBar { BorderRadius = 4 } };
        options.Xaxis = new XAxis { TickAmount = 6, Labels = ChartTheme.FlatLabels() };

        // Whole Counts Only, No Repeated Tick Labels
        options.Yaxis =
        [
            new YAxis
            {
                Min = 0,
                StepSize = 1,
                DecimalsInFloat = 0,
            },
        ];
        return options;
    }

    private static ApexChartOptions<ChartPoint> BuildSpeedOptions()
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.PlotOptions = new PlotOptions { Bar = new PlotOptionsBar { BorderRadius = 4 } };
        options.Yaxis =
        [
            new YAxis
            {
                Min = 0,
                Labels = new YAxisLabels
                {
                    Formatter = "function (value) { return Math.round(value) + ' Mbps'; }",
                },
            },
        ];
        return options;
    }

    private static ApexChartOptions<ChartPoint> BuildHeatmapOptions()
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.Theme.Monochrome.Enabled = false;
        options.Colors = [ChartTheme.WarningColor];

        // Empty Cells Sink Into The Page, Busier Ones Glow Amber
        options.PlotOptions = new PlotOptions
        {
            Heatmap = new PlotOptionsHeatmap
            {
                Radius = 4,
                EnableShades = true,
                ColorScale = new PlotOptionsHeatmapColorScale
                {
                    Ranges =
                    [
                        new PlotOptionsHeatmapColorScaleRange
                        {
                            From = 0,
                            To = 0,
                            Color = ChartTheme.EmptyColor,
                        },
                    ],
                },
            },
        };
        options.Stroke = new Stroke { Width = 2, Colors = [ChartTheme.PanelColor] };
        options.Grid = new Grid { Show = false };
        options.Legend = new Legend { Show = false };
        options.Xaxis = new XAxis { TickAmount = 12 };
        return options;
    }

    private static ApexChartOptions<TimelineBar> BuildTimelineOptions()
    {
        var options = ChartTheme.BuildBaseOptions<TimelineBar>();
        options.Theme.Monochrome.Enabled = false;
        options.PlotOptions = new PlotOptions
        {
            Bar = new PlotOptionsBar { Horizontal = true, BorderRadius = 4 },
        };
        options.Xaxis = new XAxis { Type = XAxisType.Datetime };
        options.Legend = new Legend { Show = false };
        return options;
    }
    #endregion

    public void Dispose()
    {
        Monitor.StatusChanged -= OnStatusChanged;
        GC.SuppressFinalize(this);
    }

    private sealed record HeatmapRow(string Name, List<ChartPoint> Points);
}
