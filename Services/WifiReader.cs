using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WifiWatch.Data;

namespace WifiWatch.Services;

public sealed record WifiReading(
    bool IsBlocked,
    bool IsConnected,
    string? Ssid,
    string? Band,
    int? Channel,
    int? Rssi,
    int? ReceiveRateMbps,
    int? TransmitRateMbps
)
{
    public bool IsDfs => WifiReader.IsDfsChannel(Channel);
}

public static partial class WifiReader
{
    static WifiReader() => Debug.Assert(SelfTestPasses());

    public static async Task<WifiReading> ReadAsync()
    {
        using var process = Process.Start(
            new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            }
        )!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return Parse(output);
    }

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

        // Rates Can Carry Decimals Like 286.8
        int? ReadNumber(string key) =>
            values.TryGetValue(key, out var value)
            && double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var number)
                ? (int)Math.Round(number)
                : null;

        // Older Builds Print No Rssi, Estimate It From Signal
        var rssi = ReadNumber("Rssi") ?? (ReadNumber("Signal") / 2 - 100);

        return new(
            false,
            values.GetValueOrDefault("State") == "connected",
            values.GetValueOrDefault("SSID"),
            values.GetValueOrDefault("Band"),
            ReadNumber("Channel"),
            rssi,
            ReadNumber("Receive rate (Mbps)"),
            ReadNumber("Transmit rate (Mbps)")
        );
    }

    public static bool IsDfsChannel(int? channel)
    {
        // Ponytail: Number Only, Fine Without A 6 GHz Radio
        return channel is >= 52 and <= 144;
    }

    public static (EventKind Kind, string Message) DescribeChannelChange(int? from, int? to) =>
        (IsDfsChannel(from), IsDfsChannel(to)) switch
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
            && DescribeChannelChange(100, 36).Kind == EventKind.DfsEviction
            && DescribeChannelChange(44, 100).Kind == EventKind.DfsReturn
            && DescribeChannelChange(36, 44).Kind == EventKind.ChannelChange
            && DescribeChannelChange(100, 52).Kind == EventKind.ChannelChange;
    }

    [GeneratedRegex(@"^\s*(?<key>[^:\r\n]+?)\s+:\s(?<value>.*)$", RegexOptions.Multiline)]
    private static partial Regex KeyValueLine();
}
