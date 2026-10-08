using System.Diagnostics;
using WifiWatch.Data.Enums;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Services.Integration;

public static class QuickActions
{
    // Windows Settings Pages And Tools
    public const string LocationSettings = "ms-settings:privacy-location";
    public const string WifiSettings = "ms-settings:network-wifi";
    public const string EthernetSettings = "ms-settings:network-ethernet";
    public const string NetworkStatus = "ms-settings:network-status";
    public const string WindowsUpdate = "ms-settings:windowsupdate-optionalupdates";
    public const string DeviceManager = "devmgmt.msc";

    public static string? ForEvent(EventKind kind, string? routerAdminUrl) =>
        kind switch
        {
            EventKind.LocationBlocked => LocationSettings,
            EventKind.Disconnect
            or EventKind.Reconnect
            or EventKind.ConnectFailed
            or EventKind.LinkChange => WifiSettings,
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
            EventKind.WeakSignal or EventKind.SlowLink =>
                "Move Closer To The Router Or Use A Cable",
            EventKind.PingSpike or EventKind.JitterSpike or EventKind.PacketLoss => scope switch
            {
                NetworkMonitor.ProviderScope =>
                    "Restart The Router, Call Your Provider If It Repeats",
                NetworkMonitor.HomeNetworkScope => "Check The Cable And Restart The Router",
                _ => "Move Closer, Or Test With A Cable To Rule Out Wi-Fi",
            },
            EventKind.WanDown => "Restart The Router, Call Your Provider If It Stays Down",
            EventKind.SlowDns => "Set The DNS Server To 1.1.1.1 In The Router",
            EventKind.Disconnect or EventKind.ConnectFailed =>
                "Update The Wi-Fi Driver If This Keeps Happening",
            EventKind.DfsEviction => "Set The Router To Channels 36 To 48 If This Repeats",
            EventKind.LocationBlocked => "Turn On Location For Desktop Apps",
            EventKind.DriverCheck => "Update The Wi-Fi Driver",
            _ => null,
        };

    public static string Describe(string target) =>
        target switch
        {
            LocationSettings => "Open Location Settings",
            WifiSettings => "Open Wi-Fi Settings",
            EthernetSettings => "Open Ethernet Settings",
            NetworkStatus => "Open Network Status",
            WindowsUpdate => "Open Windows Update",
            DeviceManager => "Open Device Manager",
            UpdateChecker.LatestReleaseUrl => "Open Release Page",
            _ => "Open Router Settings",
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
