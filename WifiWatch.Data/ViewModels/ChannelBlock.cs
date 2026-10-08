using WifiWatch.Data.Helpers;

namespace WifiWatch.Data.ViewModels;

public sealed record ChannelBlock(int First, int Last)
{
    public string Label =>
        WifiChannels.IsWeatherRadar(Last) ? $"{First} To {Last} Weather Radar"
        : WifiChannels.IsDfs(First) ? $"{First} To {Last} DFS"
        : $"{First} To {Last}";

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
