using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public static partial class AdapterInspector
{
    // Network Adapter Class, Where Windows Keeps Each Driver's Details
    private const string NetworkClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    // Power Plan Wireless Adapter Setting
    private const string WirelessPowerArguments =
        "/query SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a";

    private static readonly string[] s_powerSavingNames =
    [
        "Maximum Performance",
        "Low Power Saving",
        "Medium Power Saving",
        "Maximum Power Saving",
    ];

    public static async Task<AdapterInfo?> InspectAsync()
    {
        // The Real Radio, Not A Wi-Fi Direct Virtual Adapter
        var adapter = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
            )
            .OrderBy(networkInterface =>
                networkInterface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            )
            .ThenByDescending(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up
            )
            .FirstOrDefault();
        if (adapter is null)
            return null;

        var (driverVersion, driverDate) = ReadDriver(adapter.Description);
        var power = await ConsoleCommand.RunAsync("powercfg", WirelessPowerArguments);
        var roaming = await ConsoleCommand.RunAsync(
            "powershell",
            $"-NoProfile -Command \"(Get-NetAdapterAdvancedProperty -InterfaceDescription '{adapter.Description}' -ErrorAction SilentlyContinue | Where-Object DisplayName -match 'Roam' | Select-Object -First 1).DisplayValue\""
        );

        return new(
            adapter.Description,
            driverVersion,
            driverDate,
            ReadPowerSaving(power, "AC"),
            ReadPowerSaving(power, "DC"),
            string.IsNullOrWhiteSpace(roaming) ? null : roaming.Trim()
        );
    }

    private static (string? Version, DateTime? Date) ReadDriver(string description)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKey);
            foreach (var subKeyName in classKey?.GetSubKeyNames() ?? [])
            {
                using var adapterKey = classKey!.OpenSubKey(subKeyName);
                if (adapterKey?.GetValue("DriverDesc") as string != description)
                    continue;

                // The Registry Writes Dates As Month Day Year
                var date = DateTime.TryParseExact(
                    adapterKey.GetValue("DriverDate") as string,
                    "M-d-yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed
                )
                    ? parsed
                    : (DateTime?)null;
                return (adapterKey.GetValue("DriverVersion") as string, date);
            }
        }
        catch
        {
            // A Locked Key Only Means No Driver Details
        }

        return (null, null);
    }

    private static string? ReadPowerSaving(string output, string source)
    {
        var match = PowerIndex()
            .Matches(output)
            .FirstOrDefault(found => found.Groups["source"].Value == source);
        return
            match is not null
            && int.TryParse(
                match.Groups["index"].Value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var index
            )
            && index < s_powerSavingNames.Length
            ? s_powerSavingNames[index]
            : null;
    }

    [GeneratedRegex(@"Current (?<source>AC|DC) Power Setting Index: 0x(?<index>[0-9a-fA-F]+)")]
    private static partial Regex PowerIndex();
}
