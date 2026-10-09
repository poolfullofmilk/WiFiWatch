using Microsoft.AspNetCore.Components;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components.Shared.Common;

public partial class ChannelStrip
{
    [Parameter]
    public required List<ChannelBlock> Blocks { get; set; }

    [Parameter]
    public required List<NeighborReading> Neighbors { get; set; }

    [Parameter]
    public int? CurrentChannel { get; set; }

    private ChannelBlock? CurrentBlock =>
        Blocks.FirstOrDefault(block => block.Contains(CurrentChannel));

    private List<NeighborReading> On(int channel) =>
        [.. Neighbors.Where(neighbor => neighbor.Channel == channel)];

    private string CountText(int channel)
    {
        var count = On(channel).Select(neighbor => neighbor.Bssid).Distinct().Count();
        return count == 0 ? string.Empty : $"{count}";
    }

    private string CellClass(int channel)
    {
        // Neighbors Inside Your 80 MHz Block Share Your Airtime
        var isAudible = On(channel)
            .Any(neighbor => neighbor.SignalPercent >= ChannelAdvice.AudibleSignalPercent);
        var state =
            channel == CurrentChannel ? "channel-yours"
            : isAudible && CurrentBlock?.Contains(channel) == true ? "channel-shares"
            : isAudible ? "channel-used"
            : "health-nodata";
        return $"health-cell channel-cell {state}";
    }

    private string Describe(int channel)
    {
        var name = $"Channel {channel}{(WifiChannels.IsDfs(channel) ? " DFS" : string.Empty)}";
        var networks = On(channel)
            .OrderByDescending(neighbor => neighbor.SignalPercent)
            .Select(neighbor =>
                $"{(string.IsNullOrEmpty(neighbor.Ssid) ? "Hidden" : neighbor.Ssid)}, {(neighbor.SignalPercent / 2) - 100} dBm"
            )
            .ToList();

        // One Network Per Line, Numbered Once There Are Several
        var networkLines = networks.Count switch
        {
            0 => ["No Other Networks"],
            1 => networks,
            _ => networks.Select((network, index) => $"{index + 1}. {network}"),
        };
        List<string> lines = [name];
        if (channel == CurrentChannel)
            lines.Add("Yours");

        lines.AddRange(networkLines);
        return string.Join("\n", lines);
    }
}
