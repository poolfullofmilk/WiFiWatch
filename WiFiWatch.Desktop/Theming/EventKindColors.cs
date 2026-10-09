using MudBlazor;
using WiFiWatch.Data.Enums;

namespace WiFiWatch.Desktop.Theming;

public static class EventKindColors
{
    public static Color For(EventKind kind) =>
        kind switch
        {
            EventKind.DfsEviction
            or EventKind.Disconnect
            or EventKind.WanDown
            or EventKind.MonitorFailed => Color.Error,
            EventKind.ChannelChange
            or EventKind.BandChange
            or EventKind.ConnectFailed
            or EventKind.WeakSignal
            or EventKind.SlowLink
            or EventKind.PingSpike
            or EventKind.JitterSpike
            or EventKind.PacketLoss
            or EventKind.SlowDns
            or EventKind.LocationBlocked
            or EventKind.DriverCheck => Color.Warning,
            EventKind.DfsReturn
            or EventKind.Reconnect
            or EventKind.WanBack
            or EventKind.Recovered => Color.Success,
            EventKind.LinkChange
            or EventKind.FaultTrace
            or EventKind.UpdateAvailable
            or EventKind.SpeedTest
            or EventKind.DailySummary
            or EventKind.WeeklySummary => Color.Info,
            _ => Color.Default,
        };

    public static string IconFor(EventSeverity severity) =>
        severity switch
        {
            EventSeverity.Critical => Icons.Material.Rounded.Error,
            EventSeverity.Warning => Icons.Material.Rounded.Warning,
            _ => Icons.Material.Rounded.Info,
        };

    public static Color For(EventKind kind, EventSeverity severity)
    {
        // Rows Show Severity, Harmless Ones Stay Calm
        return severity switch
        {
            EventSeverity.Critical => Color.Error,
            EventSeverity.Warning => Color.Warning,
            _ => For(kind) is Color.Error or Color.Warning ? Color.Default : For(kind),
        };
    }
}
