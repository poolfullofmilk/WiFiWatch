using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;

namespace WiFiWatch.Data.ViewModels;

public sealed record ChannelBlock(int First, int Last)
{
    public string Label =>
        WiFiChannels.IsWeatherRadar(Last) ? AppText.Get("{0} To {1} Weather Radar", First, Last)
        : WiFiChannels.IsDfs(First) ? AppText.Get("{0} To {1} DFS", First, Last)
        : AppText.Get("{0} To {1}", First, Last);

    public IEnumerable<int> Channels
    {
        get
        {
            // 2.4 GHz Channels Step By 1, 5 GHz By 4
            var step = First < 36 ? 1 : 4;
            return Enumerable
                .Range(0, ((Last - First) / step) + 1)
                .Select(index => First + (index * step));
        }
    }

    public bool Contains(int? channel) => channel >= First && channel <= Last;
}
