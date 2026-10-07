using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace WifiWatch.Services.Monitoring;

public static class DnsProbe
{
    // A Cached Name Measures The Resolver, Not The Web
    private const string ProbeName = "www.google.com";
    private const int TimeoutMilliseconds = 2000;
    private const int DnsPort = 53;

    public static readonly IPAddress ReferenceServer = IPAddress.Parse("1.1.1.1");

    static DnsProbe() => Debug.Assert(SelfTestPasses());

    public static IPAddress? SystemServer() =>
        NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up
                && networkInterface.GetIPProperties().GatewayAddresses.Count > 0
            )
            .SelectMany(networkInterface => networkInterface.GetIPProperties().DnsAddresses)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

    public static async Task<double?> MeasureAsync(IPAddress server)
    {
        try
        {
            using var client = new UdpClient(server.AddressFamily);
            var queryId = (ushort)Random.Shared.Next(ushort.MaxValue);
            var stopwatch = Stopwatch.StartNew();
            await client.SendAsync(BuildQuery(queryId, ProbeName), new IPEndPoint(server, DnsPort));

            using var timeout = new CancellationTokenSource(TimeoutMilliseconds);
            var response = await client.ReceiveAsync(timeout.Token);
            var answeredId =
                response.Buffer.Length >= 2 ? (response.Buffer[0] << 8) | response.Buffer[1] : -1;
            return answeredId == queryId
                ? Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
                : null;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or SocketException)
        {
            return null;
        }
    }

    public static byte[] BuildQuery(ushort queryId, string name)
    {
        // Header Asks For Recursion, Then One A Record Question
        List<byte> query =
        [
            (byte)(queryId >> 8),
            (byte)queryId,
            0x01,
            0x00,
            0,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
        ];
        foreach (var label in name.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }

        query.AddRange([0, 0, 1, 0, 1]);
        return [.. query];
    }

    private static bool SelfTestPasses() =>
        BuildQuery(0x1234, "a.bc")
            .SequenceEqual<byte>([
                0x12,
                0x34,
                0x01,
                0x00,
                0,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                (byte)'a',
                2,
                (byte)'b',
                (byte)'c',
                0,
                0,
                1,
                0,
                1,
            ]);
}
