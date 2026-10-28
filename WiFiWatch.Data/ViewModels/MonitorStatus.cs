using System.Net;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;

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
        $"{AppInfo.DisplayName} {AppText.Get(Link)}"
        + (
            Reading is { IsConnected: true } reading
                ? AppText.Get(
                    " Channel {0}{1}",
                    reading.Channel,
                    (reading.IsDfs ? " DFS" : string.Empty)
                )
                : string.Empty
        )
        + (RouterRoundTrip is { } roundTrip ? $" {roundTrip} {AppText.Get("ms")}" : string.Empty);
}
