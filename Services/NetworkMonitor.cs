using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WifiWatch.Data;

namespace WifiWatch.Services;

public sealed record MonitorStatus(
    string Link,
    IPAddress? RouterAddress,
    WifiReading? Reading,
    long? RouterRoundTrip,
    long? InternetRoundTrip
)
{
    public string TrayText =>
        $"WifiWatch {Link}"
        + (
            Reading is { IsConnected: true } reading
                ? $" Channel {reading.Channel}{(reading.IsDfs ? " DFS" : string.Empty)}"
                : string.Empty
        )
        + (RouterRoundTrip is { } roundTrip ? $" {roundTrip} ms" : string.Empty);
}

public sealed class NetworkMonitor(UserSettings settings)
{
    // Links
    public const string EthernetLink = "Ethernet";
    public const string WifiLink = "Wi-Fi";
    public const string OfflineLink = "Offline";

    // Cadence And Limits
    private const int PingTimeoutMilliseconds = 1000;
    private const int WifiReadEverySeconds = 5;
    private const int WanDownAfterFailures = 5;
    private const string FiveGigahertz = "5 GHz";

    private static readonly IPAddress s_internetAddress = IPAddress.Parse("76.76.2.2");
    private static readonly TimeSpan s_sleepGap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_quietWindow = TimeSpan.FromSeconds(60);

    // Ping State
    private readonly Ping _routerPing = new();
    private readonly Ping _internetPing = new();
    private readonly List<long?> _routerRoundTrips = [];
    private readonly List<long?> _internetRoundTrips = [];
    private IPAddress? _routerAddress;
    private long? _lastRouterRoundTrip;
    private long? _lastInternetRoundTrip;
    private int _internetFailures;
    private DateTime? _wanDownSinceUtc;

    // Wi-Fi State
    private readonly List<WifiReading> _minuteReadings = [];
    private readonly HashSet<string> _activeConditions = [];
    private WifiReading? _lastReading;
    private WifiReading? _lastConnected;
    private DateTime? _disconnectedAtUtc;
    private string? _link;

    // Loop State
    private DateTime _quietUntilUtc;
    private bool _isFailing;

    public UserSettings Settings { get; set; } = settings;

    public MonitorStatus Status { get; private set; } = new(OfflineLink, null, null, null, null);

    public event Action<string, string>? Alerted;

    public event Action? StatusChanged;

    public event Action? EventRecorded;

    public void Start() => _ = RunAsync();

    #region Loop Methods
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var lastTickUtc = DateTime.UtcNow;
        var minuteUtc = TruncateToMinute(lastTickUtc);
        var tickCount = 0;
        _quietUntilUtc = lastTickUtc + s_quietWindow;

        await TryRecordAsync(
            EventKind.Started,
            $"Started WifiWatch {typeof(NetworkMonitor).Assembly.GetName().Version?.ToString(2)}",
            false
        );

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                // A Long Gap Means The PC Slept
                if (nowUtc - lastTickUtc > s_sleepGap)
                {
                    _quietUntilUtc = nowUtc + s_quietWindow;
                    await RecordAsync(
                        EventKind.Resumed,
                        $"Resumed After {FormatDuration(nowUtc - lastTickUtc)}",
                        false
                    );
                }

                lastTickUtc = nowUtc;

                if (TruncateToMinute(nowUtc) != minuteUtc)
                {
                    await CloseMinuteAsync(minuteUtc);
                    minuteUtc = TruncateToMinute(nowUtc);
                }

                await TrackLinkAsync();
                await Task.WhenAll(
                    PingAsync(),
                    tickCount++ % WifiReadEverySeconds == 0 ? ReadWifiAsync() : Task.CompletedTask
                );

                Status = new(
                    _link ?? OfflineLink,
                    _routerAddress,
                    _lastReading,
                    _lastRouterRoundTrip,
                    _lastInternetRoundTrip
                );
                StatusChanged?.Invoke();
                _isFailing = false;
            }
            catch (Exception exception)
            {
                // One Event Per Failure Streak, Never One Per Second
                if (!_isFailing)
                {
                    _isFailing = true;
                    await TryRecordAsync(
                        EventKind.MonitorFailed,
                        $"Monitor Failed {exception.GetType().Name}",
                        true
                    );
                }
            }
        }
    }

    private async Task CloseMinuteAsync(DateTime minuteUtc)
    {
        var lastReading = _minuteReadings.LastOrDefault();
        var router = Summarize(_routerRoundTrips);
        var internet = Summarize(_internetRoundTrips);
        var sample = new MinuteSample
        {
            MinuteUtc = minuteUtc,
            Link = _link ?? OfflineLink,
            Ssid = lastReading?.Ssid,
            Band = lastReading?.Band,
            Channel = lastReading?.Channel,
            Rssi = AverageReading(reading => reading.Rssi),
            ReceiveRateMbps = AverageReading(reading => reading.ReceiveRateMbps),
            TransmitRateMbps = AverageReading(reading => reading.TransmitRateMbps),
            RouterPingMilliseconds = router.Average,
            RouterJitterMilliseconds = router.Jitter,
            RouterLossPercent = router.LossPercent,
            InternetPingMilliseconds = internet.Average,
            InternetJitterMilliseconds = internet.Jitter,
            InternetLossPercent = internet.LossPercent,
        };
        _minuteReadings.Clear();
        _routerRoundTrips.Clear();
        _internetRoundTrips.Clear();

        await using (var context = new WifiDbContext())
        {
            context.MinuteSamples.Add(sample);
            await context.SaveChangesAsync();
        }

        var settings = Settings;
        var isOnline = sample.Link != OfflineLink;
        await TrackConditionAsync(
            "Weak Signal",
            sample.Rssi < settings.WeakSignalRssi,
            EventKind.WeakSignal,
            $"Weak Signal {sample.Rssi} dBm"
        );
        await TrackConditionAsync(
            "Slow Link",
            sample.ReceiveRateMbps < settings.SlowLinkMbps,
            EventKind.SlowLink,
            $"Slow Link {sample.ReceiveRateMbps} Mbps"
        );
        await TrackConditionAsync(
            "Router Ping",
            sample.RouterPingMilliseconds > settings.RouterPingMilliseconds
                || sample.RouterJitterMilliseconds > settings.JitterMilliseconds,
            EventKind.PingSpike,
            $"Router Ping {sample.RouterPingMilliseconds} ms Jitter {sample.RouterJitterMilliseconds} ms"
        );
        await TrackConditionAsync(
            "Internet Ping",
            sample.InternetPingMilliseconds > settings.InternetPingMilliseconds,
            EventKind.PingSpike,
            $"Internet Ping {sample.InternetPingMilliseconds} ms"
        );
        await TrackConditionAsync(
            "Router Loss",
            isOnline && sample.RouterLossPercent >= settings.PacketLossPercent,
            EventKind.PacketLoss,
            $"Router Loss {sample.RouterLossPercent}%"
        );
        await TrackConditionAsync(
            "Internet Loss",
            isOnline && sample.InternetLossPercent >= settings.PacketLossPercent,
            EventKind.PacketLoss,
            $"Internet Loss {sample.InternetLossPercent}%"
        );
    }
    #endregion

    #region Link Methods
    private async Task TrackLinkAsync()
    {
        var gateways = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .Select(networkInterface =>
                (
                    Type: networkInterface.NetworkInterfaceType,
                    Gateway: networkInterface
                        .GetIPProperties()
                        .GatewayAddresses.Select(gatewayAddress => gatewayAddress.Address)
                        .FirstOrDefault(address =>
                            address.AddressFamily == AddressFamily.InterNetwork
                            && !address.Equals(IPAddress.Any)
                        )
                )
            )
            .Where(entry => entry.Gateway is not null)
            .ToList();

        // Windows Prefers The Wire When Both Are Up
        var wiredGateway = gateways
            .FirstOrDefault(entry =>
                entry.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            )
            .Gateway;
        var wirelessGateway = gateways
            .FirstOrDefault(entry => entry.Type == NetworkInterfaceType.Wireless80211)
            .Gateway;
        var link =
            wiredGateway is not null ? EthernetLink
            : wirelessGateway is not null ? WifiLink
            : OfflineLink;
        _routerAddress = wiredGateway ?? wirelessGateway;

        if (_link is not null && link != _link)
        {
            await RecordAsync(EventKind.LinkChange, $"Switched To {link}", false);
        }

        _link = link;
    }

    private async Task PingAsync()
    {
        var routerTask = _routerAddress is null
            ? Task.FromResult<long?>(null)
            : SendPingAsync(_routerPing, _routerAddress);
        var internetTask = SendPingAsync(_internetPing, s_internetAddress);
        _lastRouterRoundTrip = await routerTask;
        _lastInternetRoundTrip = await internetTask;
        _routerRoundTrips.Add(_lastRouterRoundTrip);
        _internetRoundTrips.Add(_lastInternetRoundTrip);

        if (_lastInternetRoundTrip is not null)
        {
            _internetFailures = 0;
            if (_wanDownSinceUtc is { } wanDownSinceUtc)
            {
                _wanDownSinceUtc = null;
                await RecordAsync(
                    EventKind.WanBack,
                    $"Internet Back After {FormatDuration(DateTime.UtcNow - wanDownSinceUtc)}",
                    false
                );
            }
        }
        else if (_lastRouterRoundTrip is not null && ++_internetFailures == WanDownAfterFailures)
        {
            // Router Answering While Internet Fails Points At The Line
            _wanDownSinceUtc = DateTime.UtcNow.AddSeconds(-WanDownAfterFailures);
            await RecordAsync(EventKind.WanDown, "Internet Down While Router Answers", true);
        }
    }

    private static async Task<long?> SendPingAsync(Ping ping, IPAddress address)
    {
        try
        {
            var reply = await ping.SendPingAsync(address, PingTimeoutMilliseconds);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }
    #endregion

    #region Wi-Fi Methods
    private async Task ReadWifiAsync()
    {
        var reading = await WifiReader.ReadAsync();
        await TrackConditionAsync(
            "Location Access",
            reading.IsBlocked,
            EventKind.LocationBlocked,
            "Location Access Off So Wi-Fi Details Are Hidden"
        );

        if (
            !reading.IsBlocked
            && _lastReading is { IsBlocked: false } previous
            && previous.IsConnected != reading.IsConnected
        )
        {
            await RecordConnectionChangeAsync(previous, reading);
        }

        if (reading.IsConnected)
        {
            _minuteReadings.Add(reading);

            // Compared With The Last Connection So Drops Still Count
            if (_lastConnected is { } lastConnected)
            {
                await RecordRadioChangeAsync(lastConnected, reading);
            }

            _lastConnected = reading;
        }

        _lastReading = reading;
    }

    private async Task RecordConnectionChangeAsync(WifiReading previous, WifiReading current)
    {
        if (previous.IsConnected)
        {
            _disconnectedAtUtc = DateTime.UtcNow;
            await RecordAsync(
                EventKind.Disconnect,
                $"Wi-Fi Lost On {previous.Ssid}",
                _link != EthernetLink
            );
            return;
        }

        var downtime = _disconnectedAtUtc is { } disconnectedAtUtc
            ? $" After {FormatDuration(DateTime.UtcNow - disconnectedAtUtc)}"
            : string.Empty;
        await RecordAsync(
            EventKind.Reconnect,
            $"Wi-Fi Back On {current.Ssid} Channel {current.Channel}{downtime}",
            false
        );
    }

    private async Task RecordRadioChangeAsync(WifiReading previous, WifiReading current)
    {
        if (previous.Band != current.Band)
        {
            await RecordAsync(
                EventKind.BandChange,
                $"Band {previous.Band} To {current.Band} On {current.Ssid}",
                current.Band != FiveGigahertz
            );
        }
        else if (previous.Channel != current.Channel)
        {
            var (kind, message) = WifiReader.DescribeChannelChange(
                previous.Channel,
                current.Channel
            );
            await RecordAsync(kind, message, true);
        }
    }

    private int? AverageReading(Func<WifiReading, int?> selector) =>
        _minuteReadings.Average(selector) is { } average ? (int)Math.Round(average) : null;
    #endregion

    #region Record Methods
    private async Task TrackConditionAsync(
        string condition,
        bool isActive,
        EventKind kind,
        string message
    )
    {
        // Alert Once On Entry, Log Once On Recovery
        if (isActive && _activeConditions.Add(condition))
        {
            await RecordAsync(kind, message, true);
        }
        else if (!isActive && _activeConditions.Remove(condition))
        {
            await RecordAsync(EventKind.Recovered, $"{condition} Recovered", false);
        }
    }

    private async Task RecordAsync(EventKind kind, string message, bool isAlert)
    {
        var nowUtc = DateTime.UtcNow;
        await using (var context = new WifiDbContext())
        {
            context.Events.Add(
                new WifiEvent
                {
                    OccurredAtUtc = nowUtc,
                    Kind = kind,
                    Message = message,
                    IsAlert = isAlert,
                }
            );
            await context.SaveChangesAsync();
        }

        EventRecorded?.Invoke();

        // Sign In And Wake Flap, Location Still Warns At Once
        if (isAlert && (nowUtc >= _quietUntilUtc || kind == EventKind.LocationBlocked))
        {
            Alerted?.Invoke(kind.ToLabel(), message);
        }
    }

    private async Task TryRecordAsync(EventKind kind, string message, bool isAlert)
    {
        try
        {
            await RecordAsync(kind, message, isAlert);
        }
        catch
        {
            // A Broken Database Must Not Stop Watching
        }
    }

    private static (double? Average, double? Jitter, double LossPercent) Summarize(
        List<long?> roundTrips
    )
    {
        var replies = roundTrips.OfType<long>().ToList();
        var lossPercent =
            roundTrips.Count == 0
                ? 0
                : Math.Round(100.0 * (roundTrips.Count - replies.Count) / roundTrips.Count, 1);
        if (replies.Count == 0)
            return (null, null, lossPercent);

        // Jitter Is The Mean Gap Between Consecutive Replies
        var jitter =
            replies.Count < 2
                ? 0
                : replies
                    .Zip(replies.Skip(1), (first, second) => Math.Abs(second - first))
                    .Average();

        return (Math.Round(replies.Average(), 1), Math.Round(jitter, 1), lossPercent);
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s"
        : $"{duration.Seconds}s";
    #endregion
}
