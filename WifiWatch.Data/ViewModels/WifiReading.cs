using WifiWatch.Data.Helpers;

namespace WifiWatch.Data.ViewModels;

public sealed record WifiReading(
    bool IsBlocked,
    bool IsConnected,
    string? Ssid,
    string? Band,
    int? Channel,
    int? Rssi,
    int? ReceiveRateMbps,
    int? TransmitRateMbps
)
{
    public bool IsDfs => WifiChannels.IsDfs(Channel);
}
