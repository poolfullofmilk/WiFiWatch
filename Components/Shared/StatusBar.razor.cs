using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Services;

namespace WifiWatch.Components.Shared;

public partial class StatusBar : IDisposable
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    private IEnumerable<StatusChip> Chips => BuildChips(Monitor.Status, Monitor.Settings);

    protected override void OnInitialized() => Monitor.StatusChanged += OnStatusChanged;

    private void OnStatusChanged() => InvokeAsync(StateHasChanged);

    private static IEnumerable<StatusChip> BuildChips(MonitorStatus status, UserSettings settings)
    {
        var routerSettings = QuickActions.RouterSettings(status.RouterAddress);

        yield return status.Link switch
        {
            NetworkMonitor.EthernetLink => new(
                Icons.Material.Rounded.SettingsEthernet,
                status.Link,
                Color.Success,
                QuickActions.EthernetSettings
            ),
            NetworkMonitor.WifiLink => new(
                Icons.Material.Rounded.Wifi,
                status.Link,
                Color.Success,
                QuickActions.WifiSettings
            ),
            _ => new(
                Icons.Material.Rounded.WifiOff,
                status.Link,
                Color.Error,
                QuickActions.WifiSettings
            ),
        };

        if (status.Reading is { IsBlocked: true })
        {
            yield return new(
                Icons.Material.Rounded.LocationOff,
                "Location Off",
                Color.Warning,
                QuickActions.LocationSettings
            );
        }

        if (status.Reading is { IsConnected: true } reading)
        {
            yield return new(
                Icons.Material.Rounded.Router,
                reading.Ssid ?? "-",
                Color.Default,
                QuickActions.WifiSettings
            );
            yield return new(
                Icons.Material.Rounded.Radar,
                $"Channel {reading.Channel} {(reading.IsDfs ? "DFS" : "Non DFS")}",
                reading.IsDfs ? Color.Info : Color.Default,
                routerSettings
            );
            yield return new(
                Icons.Material.Rounded.SignalCellularAlt,
                $"{reading.Rssi} dBm",
                reading.Rssi < settings.WeakSignalRssi ? Color.Warning : Color.Success,
                null
            );
            yield return new(
                Icons.Material.Rounded.Speed,
                $"{reading.ReceiveRateMbps} Mbps",
                reading.ReceiveRateMbps < settings.SlowLinkMbps ? Color.Warning : Color.Success,
                null
            );
        }

        yield return new(
            Icons.Material.Rounded.Home,
            $"Router {FormatRoundTrip(status.RouterRoundTrip)}",
            HealthColor(status.RouterRoundTrip, settings.RouterPingMilliseconds),
            routerSettings
        );
        yield return new(
            Icons.Material.Rounded.Public,
            $"Internet {FormatRoundTrip(status.InternetRoundTrip)}",
            HealthColor(status.InternetRoundTrip, settings.InternetPingMilliseconds),
            QuickActions.NetworkStatus
        );
    }

    private static Color HealthColor(long? roundTrip, int thresholdMilliseconds)
    {
        // Lost Is Broken, Over The Threshold Is Degraded
        return roundTrip is null ? Color.Error
            : roundTrip > thresholdMilliseconds ? Color.Warning
            : Color.Success;
    }

    private static string FormatRoundTrip(long? roundTrip) =>
        roundTrip is null ? "Lost" : $"{roundTrip} ms";

    public void Dispose() => Monitor.StatusChanged -= OnStatusChanged;

    private sealed record StatusChip(string Icon, string Text, Color Color, string? Target);
}
