using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public static class FaultTracer
{
    // Like MTR, Several Probes Per Hop Show Path Loss
    private const int MaximumHops = 30;
    private const int ProbesPerHop = 3;
    private const int ProbeTimeoutMilliseconds = 800;

    // Four Silent Hops In A Row Mean The Path Ended
    private const int SilentHopLimit = 4;

    private static readonly TimeSpan s_nameTimeout = TimeSpan.FromSeconds(1);

    public static async Task<(string Summary, List<TraceHop> Hops)> TraceAsync(
        IPAddress target,
        IPAddress? routerAddress
    )
    {
        using var ping = new Ping();
        var buffer = new byte[32];
        List<(int Number, IPAddress? Address, List<double> Times)> probes = [];
        var isReached = false;
        var silentHops = 0;

        // Each Hop Answers Once Its Time To Live Runs Out
        for (var hop = 1; hop <= MaximumHops && !isReached && silentHops < SilentHopLimit; hop++)
        {
            IPAddress? address = null;
            List<double> times = [];
            for (var probe = 0; probe < ProbesPerHop; probe++)
            {
                var stopwatch = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(
                    target,
                    ProbeTimeoutMilliseconds,
                    buffer,
                    new PingOptions(hop, true)
                );
                if (reply.Status is IPStatus.Success or IPStatus.TtlExpired)
                {
                    address = reply.Address;
                    times.Add(stopwatch.Elapsed.TotalMilliseconds);
                    isReached |= reply.Status == IPStatus.Success;
                }
            }

            probes.Add((hop, address, times));
            silentHops = address is null ? silentHops + 1 : 0;
        }

        var names = await Task.WhenAll(probes.Select(probe => ResolveNameAsync(probe.Address)));
        var hops = probes
            .Select(
                (probe, index) =>
                    new TraceHop(
                        probe.Number,
                        probe.Address?.ToString(),
                        names[index],
                        Math.Round(100.0 * (ProbesPerHop - probe.Times.Count) / ProbesPerHop),
                        probe.Times.Count > 0 ? Math.Round(probe.Times.Average(), 1) : null
                    )
            )
            .ToList();

        return (Describe(hops, isReached, routerAddress), hops);
    }

    private static string Describe(List<TraceHop> hops, bool isReached, IPAddress? routerAddress)
    {
        var lastAnswer = hops.LastOrDefault(hop => hop.Address is not null);
        if (isReached)
            return $"Path Clear To The Internet In {hops.Count} Hops";

        if (lastAnswer is null)
            return "No Hop Answered, This PC Is Offline";

        return lastAnswer.Address == routerAddress?.ToString()
            ? "Stops At Your Router, The Line To The Provider Is Down"
            : $"Stops After Hop {lastAnswer.Number} {lastAnswer.Name ?? lastAnswer.Address}, Inside The Provider Network";
    }

    private static async Task<string?> ResolveNameAsync(IPAddress? address)
    {
        if (address is null)
            return null;

        try
        {
            var lookup = Dns.GetHostEntryAsync(address);
            return await Task.WhenAny(lookup, Task.Delay(s_nameTimeout)) == lookup
                ? lookup.Result.HostName
                : null;
        }
        catch
        {
            // A Hop Without A Name Is Still A Hop
            return null;
        }
    }
}
