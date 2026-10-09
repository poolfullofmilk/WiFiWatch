using System.Net;
using WiFiWatch.Data.Helpers;

namespace WiFiWatch.Data.ViewModels;

public sealed record MonitorStatus(
    string Link,
    IPAddress? RouterAddress,
    string? RouterAdminUrl,
    WiFiReading? Reading,
    long? RouterRoundTrip,
    long? InternetRoundTrip,
    DateTime? DfsFreeAtUtc,
    bool IsSpeedTesting
)
{
    public string TrayText =>
        $"{AppInfo.DisplayName} {Link}"
        + (
            Reading is { IsConnected: true } reading
                ? $" Channel {reading.Channel}{(reading.IsDfs ? " DFS" : string.Empty)}"
                : string.Empty
        )
        + (RouterRoundTrip is { } roundTrip ? $" {roundTrip} ms" : string.Empty);
}
