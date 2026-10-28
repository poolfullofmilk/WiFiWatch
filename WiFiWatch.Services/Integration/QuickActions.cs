using System.Diagnostics;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Localization;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Services.Integration;

public static class QuickActions
{
    // Windows Settings Pages And Tools
    public const string LocationSettings = "ms-settings:privacy-location";
    public const string WiFiSettings = "ms-settings:network-wifi";
    public const string EthernetSettings = "ms-settings:network-ethernet";
    public const string NetworkStatus = "ms-settings:network-status";
    public const string WindowsUpdate = "ms-settings:windowsupdate-optionalupdates";
    public const string DeviceManager = "devmgmt.msc";

    public static string? ForEvent(EventKind kind, string? scope, string? routerAdminUrl) =>
        kind switch
        {
            EventKind.LocationBlocked => LocationSettings,
            EventKind.Disconnect when scope == NetworkMonitor.HomeNetworkScope => EthernetSettings,
            EventKind.Disconnect
            or EventKind.Reconnect
            or EventKind.ConnectFailed
            or EventKind.LinkChange => WiFiSettings,
            EventKind.WanDown or EventKind.WanBack or EventKind.FaultTrace or EventKind.SlowDns =>
                NetworkStatus,
            EventKind.DriverCheck => WindowsUpdate,
            EventKind.UpdateAvailable => UpdateChecker.LatestReleaseUrl,
            EventKind.Started
            or EventKind.Resumed
            or EventKind.MonitorFailed
            or EventKind.Recovered
            or EventKind.SpeedTest
            or EventKind.DailySummary
            or EventKind.WeeklySummary => null,
            _ => routerAdminUrl,
        };

    public static string? AdviceFor(EventKind kind, string? scope) =>
        kind switch
        {
            EventKind.WeakSignal or EventKind.SlowLink => AppText.Get(
                "Move Closer To The Router Or Use A Cable"
            ),
            EventKind.PingSpike or EventKind.JitterSpike or EventKind.PacketLoss => scope switch
            {
                NetworkMonitor.ProviderScope => AppText.Get(
                    "Restart The Router, Call Your Provider If It Repeats"
                ),
                NetworkMonitor.HomeNetworkScope => AppText.Get(
                    "Check Cables And Powerline Adapters, Then Restart The Router"
                ),
                _ => AppText.Get("Move Closer, Or Test With A Cable To Rule Out Wi-Fi"),
            },
            EventKind.WanDown => AppText.Get(
                "Restart The Router, Call Your Provider If It Stays Down"
            ),
            EventKind.SlowDns => AppText.Get("Set The DNS Server To 1.1.1.1 In The Router"),
            EventKind.Disconnect when scope == NetworkMonitor.HomeNetworkScope => AppText.Get(
                "Check The Cable Or Powerline Adapter, Then The Router"
            ),
            EventKind.Disconnect or EventKind.ConnectFailed => AppText.Get(
                "Update The Wi-Fi Driver If This Keeps Happening"
            ),
            EventKind.DfsEviction => AppText.Get(
                "Set The Router To Channels 36 To 48 If This Repeats"
            ),
            EventKind.LocationBlocked => AppText.Get("Turn On Location For Desktop Apps"),
            EventKind.DriverCheck => AppText.Get("Update The Wi-Fi Driver"),
            _ => null,
        };

    public static string Describe(string target) =>
        target switch
        {
            LocationSettings => AppText.Get("Open Location Settings"),
            WiFiSettings => AppText.Get("Open Wi-Fi Settings"),
            EthernetSettings => AppText.Get("Open Ethernet Settings"),
            NetworkStatus => AppText.Get("Open Network Status"),
            WindowsUpdate => AppText.Get("Open Windows Update"),
            DeviceManager => AppText.Get("Open Device Manager"),
            UpdateChecker.LatestReleaseUrl => AppText.Get("Open Release Page"),
            _ => AppText.Get("Open Router Settings"),
        };

    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // A Missing Handler Must Not Crash The Page
        }
    }
}
