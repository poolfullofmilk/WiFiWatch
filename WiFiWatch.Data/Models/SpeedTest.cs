using Microsoft.EntityFrameworkCore;

namespace WiFiWatch.Data.Models;

[Index(nameof(TestedAtUtc))]
public sealed class SpeedTest
{
    public int Id { get; set; }

    public DateTime TestedAtUtc { get; set; }

    public required string Link { get; set; }

    public double DownloadMbps { get; set; }

    public double UploadMbps { get; set; }

    public double? IdlePingMilliseconds { get; set; }

    public double? DownloadPingMilliseconds { get; set; }

    public double? UploadPingMilliseconds { get; set; }

    public required string Grade { get; set; }
}
