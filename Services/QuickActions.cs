using System.Diagnostics;
using System.Net;
using WifiWatch.Data;

namespace WifiWatch.Services;

public static class QuickActions
{
    // Windows Settings Pages
    public const string LocationSettings = "ms-settings:privacy-location";
    public const string WifiSettings = "ms-settings:network-wifi";
    public const string EthernetSettings = "ms-settings:network-ethernet";
    public const string NetworkStatus = "ms-settings:network-status";

    public static string? RouterSettings(IPAddress? routerAddress)
    {
        // Ponytail: ASUS Admin Port, Other Routers Need Their Own
        return routerAddress is null ? null : $"https://{routerAddress}:8443";
    }

    public static string? ForEvent(EventKind kind, IPAddress? routerAddress) =>
        kind switch
        {
            EventKind.LocationBlocked => LocationSettings,
            EventKind.Disconnect or EventKind.Reconnect or EventKind.LinkChange => WifiSettings,
            EventKind.WanDown or EventKind.WanBack => NetworkStatus,
            EventKind.Started or EventKind.Resumed or EventKind.MonitorFailed => null,
            _ => RouterSettings(routerAddress),
        };

    public static string Describe(string target) =>
        target switch
        {
            LocationSettings => "Open Location Settings",
            WifiSettings => "Open Wi-Fi Settings",
            EthernetSettings => "Open Ethernet Settings",
            NetworkStatus => "Open Network Status",
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
