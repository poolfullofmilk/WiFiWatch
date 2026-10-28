using System.Diagnostics;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public static class ChannelAdvice
{
    // Neighbors Below This Barely Reach Us
    public const int AudibleSignalPercent = 30;
    private const int FrequentEvictions = 2;

    // Ponytail: EU 80 MHz Blocks, None From 132 Up
    public static readonly ChannelBlock[] Blocks =
    [
        new(36, 48),
        new(52, 64),
        new(100, 112),
        new(116, 128),
    ];

    static ChannelAdvice() => Debug.Assert(SelfTestPasses());

    public static Dictionary<ChannelBlock, int> CountNeighbors(
        IEnumerable<(string Bssid, int Channel, int SignalPercent)> neighbors
    )
    {
        var audibleNeighbors = neighbors
            .Where(neighbor => neighbor.SignalPercent >= AudibleSignalPercent)
            .ToList();

        return Blocks.ToDictionary(
            block => block,
            block =>
                audibleNeighbors
                    .Where(neighbor => block.Contains(neighbor.Channel))
                    .Select(neighbor => neighbor.Bssid)
                    .Distinct()
                    .Count()
        );
    }

    public static string Advise(
        int? currentChannel,
        Dictionary<ChannelBlock, int> neighborsPerBlock,
        int evictionCount
    )
    {
        var currentBlock = Blocks.FirstOrDefault(block => block.Contains(currentChannel));
        if (currentBlock is null)
            return AppText.Get("Not On A 5 GHz 80 MHz Channel");

        // Radar Beats Congestion When Stability Matters Most
        if (WiFiChannels.IsDfs(currentChannel) && evictionCount >= FrequentEvictions)
            return AppText.Get(
                "Radar Moved You {0} Times, 36 To 48 Never Sees Radar",
                evictionCount
            );

        // Ties Keep The Current Block, Then Prefer No Radar
        var quietestBlock = Blocks
            .OrderBy(block => neighborsPerBlock[block])
            .ThenByDescending(block => block == currentBlock)
            .ThenBy(block => WiFiChannels.IsDfs(block.First))
            .First();

        return quietestBlock == currentBlock
            ? AppText.Get("Channel {0} Sits On The Quietest Block, Stay", currentChannel)
            : AppText.Get(
                "{0} Has {1}, Yours Has {2}",
                quietestBlock.Label,
                Formatter.FormatCount(neighborsPerBlock[quietestBlock], "Neighbor"),
                neighborsPerBlock[currentBlock]
            );
    }

    private static bool SelfTestPasses()
    {
        var neighborsPerBlock = CountNeighbors([
            ("a", 36, 80),
            ("b", 40, 50),
            ("b", 44, 50),
            ("c", 100, 10),
            ("d", 116, 90),
        ]);

        return neighborsPerBlock[Blocks[0]] == 2
            && neighborsPerBlock[Blocks[2]] == 0
            && neighborsPerBlock[Blocks[3]] == 1
            && Blocks[0].Channels.SequenceEqual([36, 40, 44, 48])
            && new ChannelBlock(1, 13).Channels.Count() == 13
            && Blocks[3].Label == AppText.Get("{0} To {1} Weather Radar", 116, 128);
    }
}
