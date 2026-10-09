using Microsoft.EntityFrameworkCore;

namespace WiFiWatch.Data.Models;

[Index(nameof(MinuteUtc))]
public sealed class MinuteSample
{
    public int Id { get; set; }

    public DateTime MinuteUtc { get; set; }

    public required string Link { get; set; }

    public string? Ssid { get; set; }

    public string? Band { get; set; }

    public int? Channel { get; set; }

    public int? Rssi { get; set; }

    public int? ReceiveRateMbps { get; set; }

    public int? TransmitRateMbps { get; set; }

    public double? RouterPingMilliseconds { get; set; }

    public double? RouterJitterMilliseconds { get; set; }

    public double RouterLossPercent { get; set; }

    public double? InternetPingMilliseconds { get; set; }

    public double? InternetJitterMilliseconds { get; set; }

    public double InternetLossPercent { get; set; }

    public double? DnsMilliseconds { get; set; }

    public double? ReferenceDnsMilliseconds { get; set; }
}
