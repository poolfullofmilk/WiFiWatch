using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public static partial class WiFiReader
{
    // netsh Field Names, Printed In The Windows Language, Never Translated By The App
    private const string StateLabel = "State";
    private const string ConnectedValue = "connected";
    private const string SsidLabel = "SSID";
    private const string BandLabel = "Band";
    private const string ChannelLabel = "Channel";
    private const string SignalLabel = "Signal";
    private const string RssiLabel = "Rssi";
    private const string ReceiveRateLabel = "Receive rate (Mbps)";
    private const string TransmitRateLabel = "Transmit rate (Mbps)";
    private const string ChannelUtilizationLabel = "Channel Utilization";
    private const string BssidLabel = "BSSID";
    private const string SsidPrefix = "SSID ";
    private const string BssidPrefix = "BSSID ";
    private const string LocationPermissionText = "location permission";

    static WiFiReader() => Debug.Assert(SelfTestPasses());

    public static async Task<WiFiReading> ReadAsync() =>
        Parse(await ConsoleCommand.RunAsync("netsh", "wlan show interfaces"));

    public static async Task<List<NeighborReading>> ReadNeighborsAsync() =>
        ParseNeighbors(await ConsoleCommand.RunAsync("netsh", "wlan show networks mode=bssid"));

    public static WiFiReading Parse(string output)
    {
        // Ponytail: English Labels Only, Native WiFi API Otherwise
        // Windows 11 Hides Wi-Fi Details Without Location Access
        if (output.Contains(LocationPermissionText, StringComparison.OrdinalIgnoreCase))
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
        var rssi = ReadNumber(values, RssiLabel) ?? (ReadNumber(values, SignalLabel) / 2 - 100);

        return new(
            false,
            values.GetValueOrDefault(StateLabel) == ConnectedValue,
            values.GetValueOrDefault(SsidLabel),
            values.GetValueOrDefault(BandLabel),
            ReadNumber(values, ChannelLabel),
            rssi,
            ReadNumber(values, ReceiveRateLabel),
            ReadNumber(values, TransmitRateLabel)
        );
    }

    public static List<NeighborReading> ParseNeighbors(string output)
    {
        List<NeighborReading> neighbors = [];
        var ssid = string.Empty;
        Dictionary<string, string>? bssidValues = null;

        void AddPendingBssid()
        {
            if (bssidValues is not null && ReadNumber(bssidValues, ChannelLabel) is { } channel)
            {
                neighbors.Add(
                    new(
                        ssid,
                        bssidValues[BssidLabel],
                        channel,
                        ReadNumber(bssidValues, SignalLabel) ?? 0,
                        bssidValues.TryGetValue(ChannelUtilizationLabel, out var utilization)
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
            if (key.StartsWith(SsidPrefix, StringComparison.Ordinal))
            {
                AddPendingBssid();
                ssid = value;
            }
            else if (key.StartsWith(BssidPrefix, StringComparison.Ordinal))
            {
                AddPendingBssid();
                bssidValues = new(StringComparer.OrdinalIgnoreCase) { [BssidLabel] = value };
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

    private static bool SelfTestPasses()
    {
        // netsh Labels Are Protocol Text, So This Passes In Every App Language
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
            && neighbors[2] is { Bssid: "0a:0b:0c:0d:0e:10", SignalPercent: 20 };
    }

    [GeneratedRegex(@"^\s*(?<key>[^:\r\n]+?)\s+:\s(?<value>.*)$", RegexOptions.Multiline)]
    private static partial Regex KeyValueLine();

    [GeneratedRegex(@"^\s*(?<key>[^:\r\n]+?)\s*:[ \t]*(?<value>.*)$", RegexOptions.Multiline)]
    private static partial Regex NeighborLine();

    [GeneratedRegex(@"\((?<percent>\d+)\s*%\)")]
    private static partial Regex UtilizationPercent();
}
