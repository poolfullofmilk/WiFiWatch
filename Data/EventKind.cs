using System.Text.RegularExpressions;

namespace WifiWatch.Data;

public enum EventKind
{
    Started,
    Resumed,
    DfsEviction,
    DfsReturn,
    ChannelChange,
    BandChange,
    Disconnect,
    Reconnect,
    LinkChange,
    WeakSignal,
    SlowLink,
    PingSpike,
    PacketLoss,
    WanDown,
    WanBack,
    FaultTrace,
    Recovered,
    LocationBlocked,
    MonitorFailed,
    UpdateAvailable,
}

public static partial class EventKindExtensions
{
    public static string ToLabel(this EventKind kind) =>
        WordBoundary().Replace(kind.ToString(), " ").Replace("Dfs", "DFS").Replace("Wan", "WAN");

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
