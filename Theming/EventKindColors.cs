using MudBlazor;
using WifiWatch.Data;

namespace WifiWatch.Theming;

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
            or EventKind.WeakSignal
            or EventKind.SlowLink
            or EventKind.PingSpike
            or EventKind.PacketLoss
            or EventKind.LocationBlocked => Color.Warning,
            EventKind.DfsReturn
            or EventKind.Reconnect
            or EventKind.WanBack
            or EventKind.Recovered => Color.Success,
            EventKind.LinkChange or EventKind.FaultTrace or EventKind.UpdateAvailable => Color.Info,
            _ => Color.Default,
        };
}
