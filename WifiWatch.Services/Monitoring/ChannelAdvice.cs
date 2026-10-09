using System.Diagnostics;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

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
            return "Not On A 5 GHz 80 MHz Channel";

        // Radar Beats Congestion When Stability Matters Most
        if (WifiChannels.IsDfs(currentChannel) && evictionCount >= FrequentEvictions)
            return $"Radar Moved You {evictionCount} Times, 36 To 48 Never Sees Radar";

        // Ties Keep The Current Block, Then Prefer No Radar
        var quietestBlock = Blocks
            .OrderBy(block => neighborsPerBlock[block])
            .ThenByDescending(block => block == currentBlock)
            .ThenBy(block => WifiChannels.IsDfs(block.First))
            .First();

        return quietestBlock == currentBlock
            ? $"Channel {currentChannel} Sits On The Quietest Block, Stay"
            : $"{quietestBlock.Label} Has {Formatter.FormatCount(neighborsPerBlock[quietestBlock], "Neighbor")}, Yours Has {neighborsPerBlock[currentBlock]}";
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
            && Advise(100, neighborsPerBlock, 0).Contains("Quietest")
            && Advise(100, neighborsPerBlock, 3).StartsWith("Radar Moved You 3")
            && Advise(36, neighborsPerBlock, 3).StartsWith("52 To 64 DFS Has 0")
            && Advise(6, neighborsPerBlock, 0).StartsWith("Not On")
            && Blocks[0].Channels.SequenceEqual([36, 40, 44, 48])
            && new ChannelBlock(1, 13).Channels.Count() == 13
            && Blocks[3].Label == "116 To 128 Weather Radar";
    }
}
