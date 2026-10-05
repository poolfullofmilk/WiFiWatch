using Microsoft.EntityFrameworkCore;

namespace WifiWatch.Data;

[Index(nameof(ScanUtc))]
public sealed class NeighborSample
{
    public int Id { get; set; }

    public DateTime ScanUtc { get; set; }

    public required string Ssid { get; set; }

    public required string Bssid { get; set; }

    public int Channel { get; set; }

    public int SignalPercent { get; set; }

    public int? ChannelUtilizationPercent { get; set; }

    public bool IsOwn { get; set; }
}
