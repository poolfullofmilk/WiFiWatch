using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Localization;

namespace WiFiWatch.Data.Helpers;

public static class EventKindExtensions
{
    public static string ToLabel(this EventKind kind) =>
        AppText.Get(
            kind switch
            {
                EventKind.Started => "Started",
                EventKind.Resumed => "Resumed",
                EventKind.DfsEviction => "DFS Eviction",
                EventKind.DfsReturn => "DFS Return",
                EventKind.ChannelChange => "Channel Change",
                EventKind.BandChange => "Band Change",
                EventKind.Disconnect => "Disconnect",
                EventKind.Reconnect => "Reconnect",
                EventKind.ConnectFailed => "Connect Failed",
                EventKind.LinkChange => "Link Change",
                EventKind.WeakSignal => "Weak Signal",
                EventKind.SlowLink => "Slow Link",
                EventKind.PingSpike => "Ping Spike",
                EventKind.JitterSpike => "Jitter Spike",
                EventKind.PacketLoss => "Packet Loss",
                EventKind.SlowDns => "Slow DNS",
                EventKind.WanDown => "Internet Down",
                EventKind.WanBack => "Internet Back",
                EventKind.FaultTrace => "Fault Trace",
                EventKind.Recovered => "Recovered",
                EventKind.LocationBlocked => "Location Blocked",
                EventKind.DriverCheck => "Driver Check",
                EventKind.SpeedTest => "Speed Test",
                EventKind.DailySummary => "Daily Summary",
                EventKind.WeeklySummary => "Weekly Summary",
                EventKind.MonitorFailed => "Monitor Failed",
                EventKind.UpdateAvailable => "Update Available",
                _ => kind.ToString(),
            }
        );
}
