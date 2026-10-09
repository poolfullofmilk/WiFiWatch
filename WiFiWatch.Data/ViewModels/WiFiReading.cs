using WiFiWatch.Data.Helpers;

namespace WiFiWatch.Data.ViewModels;

public sealed record WiFiReading(
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
    public bool IsDfs => WiFiChannels.IsDfs(Channel);
}
