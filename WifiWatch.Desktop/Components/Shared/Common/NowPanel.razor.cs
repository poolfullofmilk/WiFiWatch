using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Theming;
using WifiWatch.Services.Integration;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Shared.Common;

public partial class NowPanel : IDisposable
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Parameter]
    public List<WifiEvent> Problems { get; set; } = [];

    private VerdictView Verdict => BuildVerdict(Monitor.Status, Problems);

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

    private static VerdictView BuildVerdict(MonitorStatus status, List<WifiEvent> problems)
    {
        if (problems.FirstOrDefault() is { } worst)
        {
            // The Worst Open Problem Speaks For All Of Them
            var more = problems.Count > 1 ? $", {problems.Count - 1} More Open" : string.Empty;
            var since =
                $"Since {Formatter.FormatLocal(worst.OccurredAtUtc, "HH:mm")} ({Formatter.FormatDuration(DateTime.UtcNow - worst.OccurredAtUtc)})";
            return new(
                worst.Kind.ToLabel(),
                $"{worst.Message}, {since}{more}",
                EventKindColors.For(worst.Kind, worst.Severity),
                EventKindColors.IconFor(worst.Severity),
                QuickActions.ForEvent(worst.Kind, status.RouterAdminUrl),
                QuickActions.AdviceFor(worst.Kind, worst.Scope)
            );
        }

        if (status.Link == NetworkMonitor.OfflineLink)
        {
            return new(
                "Offline",
                "No Wi-Fi Or Cable Connection",
                Color.Error,
                Icons.Material.Rounded.WifiOff,
                QuickActions.WifiSettings,
                "Check The Router And Cables"
            );
        }

        return new(
            "All Good",
            DescribeConnection(status),
            Color.Success,
            Icons.Material.Rounded.Check,
            null,
            null
        );
    }

    private static string DescribeConnection(MonitorStatus status)
    {
        var connection = status.Reading is { IsConnected: true } reading
            ? $"Connected To {reading.Ssid ?? "Wi-Fi"}, Channel {reading.Channel}{(reading.IsDfs ? " DFS" : string.Empty)} And {reading.Band ?? "-"}"
            : $"Connected Over {status.Link}";

        // A Radar Eviction Keeps The Router Off DFS For A While
        return status.DfsFreeAtUtc is { } freeAtUtc
            ? $"{connection}, DFS Free At {Formatter.FormatLocal(freeAtUtc, "HH:mm")}"
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
                    "Signal",
                    Formatter.FormatNumber(reading.Rssi, "dBm"),
                    isWeak ? "Weak" : "Good",
                    isWeak ? Color.Warning : Color.Default
                )
            );
            readings.Add(
                new(
                    "Wi-Fi Speed",
                    Formatter.FormatNumber(reading.ReceiveRateMbps, "Mbps"),
                    "Link To The Router",
                    Color.Default
                )
            );
        }
        else
        {
            readings.Add(new("Connection", status.Link, "No Wi-Fi Details", Color.Default));
        }

        readings.Add(
            RoundTrip("Router", status.RouterRoundTrip, settings.RouterPingMilliseconds, "Home")
        );
        readings.Add(
            RoundTrip(
                "Internet",
                status.InternetRoundTrip,
                settings.InternetPingMilliseconds,
                "To 1.1.1.1"
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
        return roundTrip is null ? new(label, "Lost", "No Answer", Color.Error)
            : roundTrip > thresholdMilliseconds
                ? new(label, $"{roundTrip} ms", $"Over {thresholdMilliseconds} ms", Color.Warning)
            : new(label, $"{roundTrip} ms", hint, Color.Default);
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
        string? Advice
    );

    private sealed record ReadingView(string Label, string Value, string Hint, Color Color);
}
