using WifiWatch.Data.Helpers;

namespace WifiWatch.Data.ViewModels;

public sealed record ChannelBlock(int First, int Last)
{
    public string Label =>
        $"{First} To {Last}{(WifiChannels.IsDfs(First) ? " DFS" : string.Empty)}";

    public bool Contains(int? channel) => channel >= First && channel <= Last;
}
