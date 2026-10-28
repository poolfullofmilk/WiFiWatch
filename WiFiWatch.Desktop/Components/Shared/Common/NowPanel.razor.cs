using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop.Theming;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class NowPanel : IDisposable
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Parameter]
    public List<WiFiEvent> Problems { get; set; } = [];

    [Parameter]
    public EventCallback RefreshRequested { get; set; }

    private VerdictView Verdict =>
        BuildVerdict(Monitor.Status, Problems, Monitor.ClearingIncidentIds);

    private string? RouterButtonUrl
    {
        get
        {
            // The Fix Button May Already Open The Router
            var routerAdminUrl = Monitor.Status.RouterAdminUrl;
            return routerAdminUrl == Verdict.FixTarget ? null : routerAdminUrl;
        }
    }

    private List<ReadingView> Readings => BuildReadings(Monitor.Status, Monitor.Settings);

    protected override void OnInitialized() => Monitor.StatusChanged += OnStatusChanged;

    private void OnStatusChanged() =>
        InvokeAsync(() =>
        {
            // Nobody Sees A Hidden Window, So It Skips The Redraw
            if (Window.IsVisible)
            {
                StateHasChanged();
            }
        });

    private static VerdictView BuildVerdict(
        MonitorStatus status,
        List<WiFiEvent> problems,
        IReadOnlyList<int> clearingIncidentIds
    )
    {
        if (problems.FirstOrDefault() is { } worst)
        {
            // The Worst Open Problem Speaks For All Of Them
            var more =
                problems.Count > 1
                    ? AppText.Get(", {0} More Open", problems.Count - 1)
                    : string.Empty;
            var since = AppText.Get(
                "Since {0} ({1})",
                Formatter.FormatLocal(worst.OccurredAtUtc, "HH:mm"),
                Formatter.FormatDuration(DateTime.UtcNow - worst.OccurredAtUtc)
            );
            var color = EventKindColors.For(worst.Kind, worst.Severity);

            // One Good Minute In, It Clears After The Next
            var isClearing = clearingIncidentIds.Contains(worst.Id);
            return new(
                worst.Kind.ToLabel(),
                $"{worst.Message}, {since}{more}",
                color,
                EventKindColors.IconFor(worst.Severity),
                QuickActions.ForEvent(worst.Kind, worst.Scope, status.RouterAdminUrl),
                isClearing
                    ? AppText.Get("Looks Fine Again, Clears After One More Good Minute")
                    : QuickActions.AdviceFor(worst.Kind, worst.Scope),
                isClearing ? Color.Success : color
            );
        }

        if (status.Link == NetworkMonitor.OfflineLink)
        {
            return new(
                AppText.Get("Offline"),
                AppText.Get("No Wi-Fi Or Cable Connection"),
                Color.Error,
                Icons.Material.Rounded.WifiOff,
                QuickActions.WiFiSettings,
                AppText.Get("Check The Router, Cables And Powerline Adapters"),
                Color.Error
            );
        }

        return new(
            AppText.Get("All Good"),
            DescribeConnection(status),
            Color.Success,
            Icons.Material.Rounded.Check,
            null,
            null,
            Color.Success
        );
    }

    private static string DescribeConnection(MonitorStatus status)
    {
        var connection = status.Reading is { IsConnected: true } reading
            ? AppText.Get(
                "Connected to {0}, Channel {1}{2} and {3}",
                reading.Ssid ?? AppText.Get("Wi-Fi"),
                reading.Channel,
                (reading.IsDfs ? " DFS" : string.Empty),
                reading.Band ?? "-"
            )
            : AppText.Get("Connected over {0}", AppText.Get(status.Link));

        // A Radar Eviction Keeps The Router Off DFS For A While
        return status.DfsFreeAtUtc is { } freeAtUtc
            ? AppText.Get(
                "{0}, DFS free at {1}",
                connection,
                Formatter.FormatLocal(freeAtUtc, "HH:mm")
            )
            : connection;
    }

    private static List<ReadingView> BuildReadings(MonitorStatus status, UserSettings settings)
    {
        List<ReadingView> readings = [];
        if (status.Reading is { IsConnected: true } reading)
        {
            var isWeak = reading.Rssi < settings.WeakSignalRssi;
            readings.Add(
                new(
                    AppText.Get("Signal"),
                    Formatter.FormatNumber(reading.Rssi, "dBm"),
                    isWeak ? AppText.Get("Weak") : AppText.Get("Good"),
                    isWeak ? Color.Warning : Color.Default
                )
            );
            readings.Add(
                new(
                    AppText.Get("Wi-Fi Speed"),
                    Formatter.FormatNumber(reading.ReceiveRateMbps, "Mbps"),
                    AppText.Get("Link To The Router"),
                    Color.Default
                )
            );
        }
        else
        {
            readings.Add(
                new(
                    AppText.Get("Connection"),
                    AppText.Get(status.Link),
                    status.Link == NetworkMonitor.EthernetLink
                        ? AppText.Get("Cable Or Powerline")
                        : AppText.Get("Not Connected"),
                    Color.Default
                )
            );
        }

        // Some Routers Never Answer Ping, Yet Pass The Internet On
        readings.Add(
            status.RouterRoundTrip is null && status.InternetRoundTrip is not null
                ? new(AppText.Get("Router"), "-", AppText.Get("No Answer"), Color.Default)
                : RoundTrip(
                    AppText.Get("Router"),
                    status.RouterRoundTrip,
                    settings.RouterPingMilliseconds,
                    AppText.Get("Home")
                )
        );
        readings.Add(
            RoundTrip(
                AppText.Get("Internet"),
                status.InternetRoundTrip,
                settings.InternetPingMilliseconds,
                AppText.Get("To 1.1.1.1")
            )
        );
        return readings;
    }

    private static ReadingView RoundTrip(
        string label,
        long? roundTrip,
        int thresholdMilliseconds,
        string hint
    )
    {
        // Lost Is Broken, Over The Threshold Is Degraded
        return roundTrip is null
                ? new(label, AppText.Get("Lost"), AppText.Get("No Answer"), Color.Error)
            : roundTrip > thresholdMilliseconds
                ? new(
                    label,
                    $"{roundTrip} {AppText.Get("ms")}",
                    AppText.Get("Over {0} ms", thresholdMilliseconds),
                    Color.Warning
                )
            : new(label, $"{roundTrip} {AppText.Get("ms")}", hint, Color.Default);
    }

    public void Dispose()
    {
        Monitor.StatusChanged -= OnStatusChanged;
        GC.SuppressFinalize(this);
    }

    private sealed record VerdictView(
        string Title,
        string Detail,
        Color Color,
        string Icon,
        string? FixTarget,
        string? Advice,
        Color AdviceColor
    );

    private sealed record ReadingView(string Label, string Value, string Hint, Color Color);
}
