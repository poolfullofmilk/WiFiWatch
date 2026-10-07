using System.Net;
using System.Net.Sockets;

namespace WifiWatch.Services.Integration;

public static class RouterAdmin
{
    // HTTPS Routers Often Move Their Admin Page To 8443
    private static readonly (int Port, string Address)[] s_candidates =
    [
        (8443, "https://{0}:8443"),
        (443, "https://{0}"),
        (80, "http://{0}"),
    ];

    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromMilliseconds(400);

    public static async Task<string> DetectAsync(IPAddress routerAddress)
    {
        foreach (var (port, address) in s_candidates)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(s_probeTimeout);
                await client.ConnectAsync(routerAddress, port, timeout.Token);
                return string.Format(address, routerAddress);
            }
            catch (Exception exception)
                when (exception is SocketException or OperationCanceledException)
            {
                // A Closed Port Means Try The Next One
            }
        }

        return $"http://{routerAddress}";
    }
}
