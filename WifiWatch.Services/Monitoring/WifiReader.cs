using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static partial class WifiReader
{
    static WifiReader() => Debug.Assert(SelfTestPasses());

    public static async Task<WifiReading> ReadAsync() =>
        Parse(await ConsoleCommand.RunAsync("netsh", "wlan show interfaces"));

    public static async Task<List<NeighborReading>> ReadNeighborsAsync() =>
        ParseNeighbors(await ConsoleCommand.RunAsync("netsh", "wlan show networks mode=bssid"));

    public static WifiReading Parse(string output)
    {
        // Ponytail: English Labels Only, Native Wifi API Otherwise
        // Windows 11 Hides Wi-Fi Details Without Location Access
        if (output.Contains("location permission", StringComparison.OrdinalIgnoreCase))
            return new(true, false, null, null, null, null, null, null);

        // First Interface Wins When Several Print
        var values = KeyValueLine()
            .Matches(output)
            .DistinctBy(match => match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                match => match.Groups["key"].Value,
                match => match.Groups["value"].Value.Trim(),
                StringComparer.OrdinalIgnoreCase
            );

        // Older Builds Print No Rssi, Estimate It From Signal
        var rssi = ReadNumber(values, "Rssi") ?? (ReadNumber(values, "Signal") / 2 - 100);

        return new(
            false,
            values.GetValueOrDefault("State") == "connected",
            values.GetValueOrDefault("SSID"),
            values.GetValueOrDefault("Band"),
            ReadNumber(values, "Channel"),
            rssi,
            ReadNumber(values, "Receive rate (Mbps)"),
            ReadNumber(values, "Transmit rate (Mbps)")
        );
    }

    public static List<NeighborReading> ParseNeighbors(string output)
    {
        List<NeighborReading> neighbors = [];
        var ssid = string.Empty;
        Dictionary<string, string>? bssidValues = null;

        void AddPendingBssid()
        {
            if (bssidValues is not null && ReadNumber(bssidValues, "Channel") is { } channel)
            {
                neighbors.Add(
                    new(
                        ssid,
                        bssidValues["BSSID"],
                        channel,
                        ReadNumber(bssidValues, "Signal") ?? 0,
                        bssidValues.TryGetValue("Channel Utilization", out var utilization)
                        && UtilizationPercent().Match(utilization) is { Success: true } match
                            ? int.Parse(match.Groups["percent"].Value)
                            : null
                    )
                );
            }

            bssidValues = null;
        }

        // Each SSID Lists Its BSSIDs, Each BSSID Its Details
        foreach (Match match in NeighborLine().Matches(output))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();
            if (key.StartsWith("SSID ", StringComparison.Ordinal))
            {
                AddPendingBssid();
                ssid = value;
            }
            else if (key.StartsWith("BSSID ", StringComparison.Ordinal))
            {
                AddPendingBssid();
                bssidValues = new(StringComparer.OrdinalIgnoreCase) { ["BSSID"] = value };
            }
            else
            {
                bssidValues?.TryAdd(key, value);
            }
        }

        AddPendingBssid();
        return neighbors;
    }

    private static int? ReadNumber(Dictionary<string, string> values, string key)
    {
        // Rates Can Carry Decimals Like 286.8
        return
            values.TryGetValue(key, out var value)
            && double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var number)
            ? (int)Math.Round(number)
            : null;
    }

    public static (EventKind Kind, string Message) DescribeChannelChange(int? from, int? to) =>
        (WifiChannels.IsDfs(from), WifiChannels.IsDfs(to)) switch
        {
            (true, false) => (EventKind.DfsEviction, $"Channel {from} To {to} Left DFS"),
            (false, true) => (EventKind.DfsReturn, $"Channel {from} To {to} Back On DFS"),
            _ => (EventKind.ChannelChange, $"Channel {from} To {to}"),
        };

    private static bool SelfTestPasses()
    {
        const string connectedOutput = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                Physical address       : 02:1a:2b:3c:4d:5e
                State                  : connected
                SSID                   : 5 GHz
                AP BSSID               : 02:1a:2b:3c:4d:5f
                Band                   : 5 GHz
                Channel                : 100
                Receive rate (Mbps)    : 1201
                Transmit rate (Mbps)   : 1081
                Signal                 : 87%
                Rssi                   : -54
            """;
        const string neighborOutput = """
            Interface name : Wi-Fi
            There are 2 networks currently visible.

            SSID 1 : 5 GHz
                Network type            : Infrastructure
                BSSID 1                 : 02:1a:2b:3c:4d:5f
                     Signal             : 87%
                     Band               : 5 GHz
                     Channel            : 100
                     Bss Load:
                         Connected Stations:        2
                         Channel Utilization:       34 (13 %)
                     Basic rates (Mbps) : 6 12 24

            SSID 2 : Neighbor
                Network type            : Infrastructure
                BSSID 1                 : 0a:0b:0c:0d:0e:0f
                     Signal             : 41%
                     Channel            : 36
                BSSID 2                 : 0a:0b:0c:0d:0e:10
                     Signal             : 20%
                     Channel            : 6
            """;
        var neighbors = ParseNeighbors(neighborOutput);

        return Parse(connectedOutput)
                is {
                    IsConnected: true,
                    Ssid: "5 GHz",
                    Band: "5 GHz",
                    Channel: 100,
                    Rssi: -54,
                    ReceiveRateMbps: 1201,
                    TransmitRateMbps: 1081,
                    IsDfs: true,
                }
            && Parse("    State                  : disconnected").IsConnected is false
            && Parse("Network shell commands need location permission to access WLAN").IsBlocked
            && Parse("    Signal                 : 80%").Rssi == -60
            && Parse("    Receive rate (Mbps)    : 286.8").ReceiveRateMbps == 287
            && neighbors.Count == 3
            && neighbors[0]
                is {
                    Ssid: "5 GHz",
                    Bssid: "02:1a:2b:3c:4d:5f",
                    Channel: 100,
                    SignalPercent: 87,
                    ChannelUtilizationPercent: 13,
                }
            && neighbors[1] is { Ssid: "Neighbor", Channel: 36, ChannelUtilizationPercent: null }
            && neighbors[2] is { Bssid: "0a:0b:0c:0d:0e:10", SignalPercent: 20 }
            && DescribeChannelChange(100, 36).Kind == EventKind.DfsEviction
            && DescribeChannelChange(44, 100).Kind == EventKind.DfsReturn
            && DescribeChannelChange(36, 44).Kind == EventKind.ChannelChange
            && DescribeChannelChange(100, 52).Kind == EventKind.ChannelChange;
    }

    [GeneratedRegex(@"^\s*(?<key>[^:\r\n]+?)\s+:\s(?<value>.*)$", RegexOptions.Multiline)]
    private static partial Regex KeyValueLine();

    [GeneratedRegex(@"^\s*(?<key>[^:\r\n]+?)\s*:[ \t]*(?<value>.*)$", RegexOptions.Multiline)]
    private static partial Regex NeighborLine();

    [GeneratedRegex(@"\((?<percent>\d+)\s*%\)")]
    private static partial Regex UtilizationPercent();
}
