using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Localization;

namespace WiFiWatch.Data.Helpers;

public static class WiFiChannels
{
    // EU Radar Rules, Fixed By ETSI EN 301 893
    public static readonly TimeSpan NonOccupancy = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ChannelCheck = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan WeatherChannelCheck = TimeSpan.FromMinutes(10);

    public static bool IsDfs(int? channel)
    {
        // Ponytail: Number Only, Fine Without A 6 GHz Radio
        return channel is >= 52 and <= 144;
    }

    public static bool IsWeatherRadar(int? channel)
    {
        // 5600 To 5650 MHz Shares The Band With Weather Radar
        return channel is >= 120 and <= 128;
    }

    public static (EventKind Kind, string Message) DescribeChange(int? from, int? to) =>
        (IsDfs(from), IsDfs(to)) switch
        {
            (true, false) => (
                EventKind.DfsEviction,
                AppText.Get("Channel {0} To {1} Left DFS", from, to)
            ),
            (false, true) => (
                EventKind.DfsReturn,
                AppText.Get("Channel {0} To {1} Back On DFS", from, to)
            ),
            _ => (EventKind.ChannelChange, AppText.Get("Channel {0} To {1}", from, to)),
        };
}
