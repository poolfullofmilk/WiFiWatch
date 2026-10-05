using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Components.Shared;
using WifiWatch.Data;
using WifiWatch.Services;
using WifiWatch.Theming;

namespace WifiWatch.Components;

public partial class Home : IDisposable
{
    private const int EventsPanelIndex = 0;

    private static readonly List<SegmentedButtonOption<StatsRange>> s_statsRangeOptions =
    [
        new(StatsRange.Daily, "Daily"),
        new(StatsRange.Weekly, "Weekly"),
    ];

    private static readonly List<FilterMenu.FilterOption> s_eventKindOptions =
    [
        .. Enum.GetValues<EventKind>()
            .Select(kind => new FilterMenu.FilterOption(
                kind.ToString(),
                kind.ToLabel(),
                EventKindColors.For(kind)
            )),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    // Events Table State
    private readonly HashSet<string> _selectedEventKinds = [.. Enum.GetNames<EventKind>()];
    private DataTable<WifiEvent>? _eventTable;
    private string _eventSearchTerm = string.Empty;
    private int _eventTotal;
    private int _activePanelIndex;
    private MudMessageBox? _closeMessageBox;

    // Minutes Table State
    private string _minuteSearchTerm = string.Empty;

    // Stats State
    private readonly ApexChartOptions<ChartPoint> _pingOptions = BuildPingOptions();
    private readonly ApexChartOptions<ChartPoint> _evictionOptions = BuildEvictionOptions();
    private StatsRange _statsRange = StatsRange.Daily;
    private bool _isStatsLoading = true;
    private int _statsRenderKey;
    private List<ChartPoint> _routerPingPoints = [];
    private List<ChartPoint> _internetPingPoints = [];
    private List<ChartPoint> _evictionPoints = [];

    protected override void OnInitialized()
    {
        Monitor.EventRecorded += OnEventRecorded;
        Window.CloseRequested += OnCloseRequested;
    }

    private Task OnCloseRequested() => InvokeAsync(AskBeforeClosingAsync);

    private async Task AskBeforeClosingAsync()
    {
        // Dismissing The Dialog Keeps Everything As It Was
        var keepRunning = await _closeMessageBox!.ShowAsync(
            new DialogOptions { CloseButton = false, BackdropClick = true }
        );

        if (keepRunning == true)
        {
            Window.Hide();
        }
        else if (keepRunning == false)
        {
            Window.Exit();
        }
    }

    #region Event Methods
    private void OnEventRecorded() => InvokeAsync(RefreshEventsAsync);

    private async Task RefreshEventsAsync()
    {
        // A Hidden Table Is Gone, So Only Its Badge Updates
        if (_activePanelIndex == EventsPanelIndex && _eventTable is not null)
        {
            await _eventTable.ReloadAsync();
            return;
        }

        await using var context = new WifiDbContext();
        _eventTotal = await FilterEvents(context).CountAsync();
        StateHasChanged();
    }

    private Task ReloadEventsAsync() => _eventTable?.ReloadAsync() ?? Task.CompletedTask;

    private async Task<TableData<WifiEvent>> LoadEventsAsync(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        await using var context = new WifiDbContext();
        var query = FilterEvents(context);
        _eventTotal = await query.CountAsync();
        var items = await query
            .OrderByColumn(
                wifiEvent => wifiEvent.OccurredAtUtc,
                state.SortDirection != SortDirection.Ascending
            )
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync();

        // Refresh The Tab Badge
        StateHasChanged();

        return new() { Items = items, TotalItems = _eventTotal };
    }

    private IQueryable<WifiEvent> FilterEvents(WifiDbContext context)
    {
        var selectedKinds = _selectedEventKinds.Select(Enum.Parse<EventKind>).ToList();
        var query = context
            .Events.AsNoTracking()
            .Where(wifiEvent => selectedKinds.Contains(wifiEvent.Kind));

        return string.IsNullOrWhiteSpace(_eventSearchTerm)
            ? query
            : query.Where(wifiEvent =>
                EF.Functions.Like(wifiEvent.Message, $"%{_eventSearchTerm}%")
            );
    }
    #endregion

    #region Minute Methods
    private async Task<TableData<MinuteSample>> LoadMinutesAsync(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        await using var context = new WifiDbContext();
        var query = context.MinuteSamples.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(_minuteSearchTerm))
        {
            var pattern = $"%{_minuteSearchTerm}%";
            query = query.Where(sample =>
                EF.Functions.Like(sample.Link, pattern) || EF.Functions.Like(sample.Ssid, pattern)
            );
        }

        var descending = state.SortDirection != SortDirection.Ascending;
        var sortedQuery = state.SortLabel switch
        {
            nameof(MinuteSample.Rssi) => query.OrderByColumn(sample => sample.Rssi, descending),
            nameof(MinuteSample.RouterPingMilliseconds) => query.OrderByColumn(
                sample => sample.RouterPingMilliseconds,
                descending
            ),
            nameof(MinuteSample.InternetPingMilliseconds) => query.OrderByColumn(
                sample => sample.InternetPingMilliseconds,
                descending
            ),
            _ => query.OrderByColumn(sample => sample.MinuteUtc, descending),
        };

        return new()
        {
            TotalItems = await query.CountAsync(),
            Items = await sortedQuery
                .Skip(state.Page * state.PageSize)
                .Take(state.PageSize)
                .ToListAsync(),
        };
    }

    private static string FormatLocal(DateTime utcTime, string format) =>
        utcTime.ToLocalTime().ToString(format);

    private static string FormatValue(double? value, string unit) =>
        value is null ? "-" : $"{value:0.#} {unit}";

    private static string FormatValue(int? value, string unit) =>
        value is null ? "-" : $"{value} {unit}";

    private static string FormatPercent(double value) => $"{value:0.#}%";

    private static string FormatChannel(int? channel) =>
        channel is null ? "-"
        : WifiReader.IsDfsChannel(channel) ? $"{channel} DFS"
        : $"{channel}";

    private static string FormatRate(MinuteSample sample) =>
        sample.ReceiveRateMbps is null
            ? "-"
            : $"{sample.ReceiveRateMbps}/{sample.TransmitRateMbps} Mbps";
    #endregion

    #region Stats Methods
    private async Task SelectStatsRangeAsync(StatsRange range)
    {
        _statsRange = range;
        await LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        _isStatsLoading = true;

        // Ping Is Hourly, Evictions Hourly Or Daily
        var isWeekly = _statsRange == StatsRange.Weekly;
        var nowUtc = DateTime.UtcNow;
        var pingHourCount = isWeekly ? 7 * 24 : 24;
        var pingStartUtc = new DateTime(
            nowUtc.Ticks - (nowUtc.Ticks % TimeSpan.TicksPerHour),
            DateTimeKind.Utc
        ).AddHours(1 - pingHourCount);
        var pingLabelFormat = isWeekly ? "ddd HH'h'" : "HH'h'";
        var (evictionBucket, evictionBucketCount, evictionStartUtc, evictionLabelFormat) = isWeekly
            ? (TimeSpan.FromDays(1), 7, DateTime.Today.AddDays(-6).ToUniversalTime(), "ddd")
            : (TimeSpan.FromHours(1), 24, pingStartUtc, "HH'h'");
        _pingOptions.Xaxis.TickAmount = isWeekly ? 14 : 12;

        await using var context = new WifiDbContext();
        var samples = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= pingStartUtc)
            .Select(sample => new
            {
                sample.MinuteUtc,
                sample.RouterPingMilliseconds,
                sample.InternetPingMilliseconds,
            })
            .ToListAsync();
        var evictionTimes = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.Kind == EventKind.DfsEviction
                && wifiEvent.OccurredAtUtc >= evictionStartUtc
            )
            .Select(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();

        // Every Bucket Shows, Empty Ones Included
        var samplesByHour = samples.ToLookup(sample =>
            BucketIndex(sample.MinuteUtc, pingStartUtc, TimeSpan.FromHours(1))
        );
        _routerPingPoints =
        [
            .. Enumerable
                .Range(0, pingHourCount)
                .Select(hour => new ChartPoint(
                    FormatLocal(pingStartUtc.AddHours(hour), pingLabelFormat),
                    (decimal?)samplesByHour[hour].Average(sample => sample.RouterPingMilliseconds)
                )),
        ];
        _internetPingPoints =
        [
            .. Enumerable
                .Range(0, pingHourCount)
                .Select(hour => new ChartPoint(
                    FormatLocal(pingStartUtc.AddHours(hour), pingLabelFormat),
                    (decimal?)samplesByHour[hour].Average(sample => sample.InternetPingMilliseconds)
                )),
        ];

        var evictionsByBucket = evictionTimes
            .CountBy(evictionTime => BucketIndex(evictionTime, evictionStartUtc, evictionBucket))
            .ToDictionary();
        _evictionPoints =
        [
            .. Enumerable
                .Range(0, evictionBucketCount)
                .Select(bucket => new ChartPoint(
                    FormatLocal(evictionStartUtc + (bucket * evictionBucket), evictionLabelFormat),
                    evictionsByBucket.GetValueOrDefault(bucket)
                )),
        ];

        // A New Key Rebuilds The Charts With The New Range
        _statsRenderKey++;
        _isStatsLoading = false;
    }

    private static int BucketIndex(DateTime utcTime, DateTime startUtc, TimeSpan bucketSize) =>
        (int)((utcTime - startUtc) / bucketSize);

    private static ApexChartOptions<ChartPoint> BuildPingOptions()
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.Stroke = new Stroke { Curve = Curve.Smooth, Width = 3 };
        options.Markers = new Markers { Size = 3 };
        options.Xaxis = new XAxis { TickAmount = 12 };
        options.Yaxis =
        [
            new YAxis
            {
                Labels = new YAxisLabels
                {
                    Formatter =
                        "function (value) { return value == null ? '' : Math.round(value) + ' ms'; }",
                },
            },
        ];
        return options;
    }

    private static ApexChartOptions<ChartPoint> BuildEvictionOptions()
    {
        var options = ChartTheme.BuildBaseOptions<ChartPoint>();
        options.PlotOptions = new PlotOptions { Bar = new PlotOptionsBar { BorderRadius = 4 } };
        options.Xaxis = new XAxis { TickAmount = 6 };
        // Whole Evictions Only, No Repeated Tick Labels
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
    #endregion

    #region Export Methods
    private async Task ExportEventsAsync(DateTime? day)
    {
        await using var context = new WifiDbContext();
        var (startUtc, endUtc) = DayRangeUtc(day);
        var events = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc && wifiEvent.OccurredAtUtc < endUtc
            )
            .OrderBy(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();

        CsvExport.Save(
            "Events",
            day,
            [
                ["Time", "Kind", "Message", "Alert"],
                .. events.Select(wifiEvent =>
                    new object?[]
                    {
                        FormatLocal(wifiEvent.OccurredAtUtc, "yyyy-MM-dd HH:mm:ss"),
                        wifiEvent.Kind.ToLabel(),
                        wifiEvent.Message,
                        wifiEvent.IsAlert ? "Yes" : "No",
                    }
                ),
            ]
        );
    }

    private async Task ExportMinutesAsync(DateTime? day)
    {
        await using var context = new WifiDbContext();
        var (startUtc, endUtc) = DayRangeUtc(day);
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
                ],
                .. samples.Select(sample =>
                    new object?[]
                    {
                        FormatLocal(sample.MinuteUtc, "yyyy-MM-dd HH:mm"),
                        sample.Link,
                        sample.Ssid,
                        sample.Band,
                        sample.Channel,
                        WifiReader.IsDfsChannel(sample.Channel) ? "Yes" : "No",
                        sample.Rssi,
                        sample.ReceiveRateMbps,
                        sample.TransmitRateMbps,
                        sample.RouterPingMilliseconds,
                        sample.RouterJitterMilliseconds,
                        sample.RouterLossPercent,
                        sample.InternetPingMilliseconds,
                        sample.InternetJitterMilliseconds,
                        sample.InternetLossPercent,
                    }
                ),
            ]
        );
    }

    private static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateTime? day) =>
        day is { } date
            ? (date.Date.ToUniversalTime(), date.Date.AddDays(1).ToUniversalTime())
            : (DateTime.MinValue, DateTime.MaxValue);
    #endregion

    private void SaveSettings(UserSettings settings)
    {
        StartupRegistration.Apply(settings.StartWithWindows);
        Monitor.Settings = settings;
        settings.Save();
    }

    private string EvictionTitle =>
        _statsRange == StatsRange.Weekly ? "DFS Evictions Per Day" : "DFS Evictions Per Hour";

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        Window.CloseRequested -= OnCloseRequested;
    }

    public enum StatsRange
    {
        Daily,
        Weekly,
    }
}
