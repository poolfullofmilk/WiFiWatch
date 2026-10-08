using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Services.Integration;
using WifiWatch.Services.Storage;

namespace WifiWatch.Services.Monitoring;

public sealed class NetworkMonitor
{
    // Links
    public const string EthernetLink = "Ethernet";
    public const string WifiLink = "Wi-Fi";
    public const string OfflineLink = "Offline";

    // Where A Problem Sits
    public const string WifiScope = "Wi-Fi";
    public const string HomeNetworkScope = "Home Network";
    public const string ProviderScope = "Internet Provider";
    public const string DnsScope = "DNS";

    // Incident Keys
    private const string WanDownKey = "WanDown";
    private const string WifiDisconnectKey = "WifiDisconnect";
    private const string WiredOfflineKey = "WiredOffline";
    private const string LocationKey = "LocationBlocked";
    private const string ConnectFailedKey = "ConnectFailed";

    // Cadence And Limits
    private const int PingTimeoutMilliseconds = 1000;
    private const int LinkCheckEverySeconds = 5;
    private const int WifiReadEverySeconds = 5;
    private const int WlanReadEverySeconds = 5;
    private const int DnsProbeEverySeconds = 15;
    private const int NeighborScanEverySeconds = 300;
    private const int WanDownAfterFailures = 5;
    private const int NightlySpeedTestHour = 3;

    // Fixed Limits, 2% Means Two Lost Pings A Minute
    private const int PacketLossPercent = 2;
    private const int SlowDnsMilliseconds = 150;
    private const string FiveGigahertz = "5 GHz";

    private static readonly IPAddress s_internetAddress = IPAddress.Parse("1.1.1.1");
    private static readonly TimeSpan s_sleepGap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_traceSpacing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_reasonWindow = TimeSpan.FromMinutes(2);

    private readonly EventJournal _journal = new();
    private readonly ConditionTracker _conditions;
    private readonly bool _isStartedAtSignIn;

    // Ping State
    private readonly Ping _routerPing = new();
    private readonly Ping _internetPing = new();
    private readonly List<long?> _routerRoundTrips = [];
    private readonly List<long?> _internetRoundTrips = [];
    private IPAddress? _routerAddress;
    private long? _lastRouterRoundTrip;
    private long? _lastInternetRoundTrip;
    private int _internetFailures;

    // DNS State
    private readonly List<double?> _dnsTimes = [];
    private readonly List<double?> _referenceDnsTimes = [];

    // Wi-Fi State
    private readonly List<WifiReading> _minuteReadings = [];
    private WifiReading? _lastReading;
    private WifiReading? _lastConnected;
    private string? _link;
    private int? _ownAirtimePercent;
    private bool _hasWarnedWeatherRadar;

    // DFS State
    private DateTime? _dfsFreeAtUtc;
    private DateTime? _evictedAtUtc;

    // Router Admin State
    private IPAddress? _adminRouterAddress;
    private string? _routerAdminUrl;

    // Diagnosis State
    private long _wlanRecordId;
    private int _connectFailures;
    private DateTime _lastTraceUtc = DateTime.MinValue;
    private DateTime _dailyJobsDate;
    private DateTime _speedTestDate;
    private DateTime _speedTestEndedUtc = DateTime.MinValue;
    private bool _isSpeedTesting;
    private bool _isFailing;
    private volatile bool _isLinkStale = true;

    public NetworkMonitor(UserSettings settings, bool isStartedAtSignIn)
    {
        Settings = settings;
        _isStartedAtSignIn = isStartedAtSignIn;
        _conditions = new(_journal);
        NetworkChange.NetworkAddressChanged += (_, _) => _isLinkStale = true;
        _conditions.IncidentOpened += TraceIncidentAsync;
        _journal.Alerted += (title, message) => Alerted?.Invoke(title, message);
        _journal.EventRecorded += () => EventRecorded?.Invoke();
        Status = new(OfflineLink, null, null, null, null, null, null, false);
    }

    public UserSettings Settings { get; set; }

    public MonitorStatus Status { get; private set; }

    public Version? AvailableUpdate { get; private set; }

    public AdapterInfo? Adapter { get; private set; }

    public event Action<string, string>? Alerted;

    public event Action? StatusChanged;

    public event Action? EventRecorded;

    public event Action? UpdateFound;

    public void Start() => _ = RunAsync();

    #region Loop Methods
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var lastTickUtc = DateTime.UtcNow;
        var minuteUtc = TruncateToMinute(lastTickUtc);
        var tickCount = 0;
        _journal.StartQuietWindow();
        _wlanRecordId = WlanEventReader.LatestRecordId();

        try
        {
            await EventJournal.CloseLeftoversAsync();
            await RecordStartedAsync();
        }
        catch
        {
            // A Broken Database Must Not Stop Watching
        }

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                // A Long Gap Means The PC Slept
                if (nowUtc - lastTickUtc > s_sleepGap)
                {
                    _journal.StartQuietWindow();
                    await _journal.RecordAsync(
                        EventKind.Resumed,
                        EventSeverity.Info,
                        $"Resumed After Sleep Of {Formatter.FormatDuration(nowUtc - lastTickUtc)}"
                    );
                }

                lastTickUtc = nowUtc;

                if (TruncateToMinute(nowUtc) != minuteUtc)
                {
                    await CloseMinuteAsync(minuteUtc);
                    minuteUtc = TruncateToMinute(nowUtc);
                }

                StartScheduledJobs(tickCount);
                _journal.AlertLongWarnings();

                var tick = tickCount++;

                // Listing Adapters Is The Costliest Step, So Only On Change
                if (_isLinkStale || tick % LinkCheckEverySeconds == 0)
                {
                    _isLinkStale = false;
                    await TrackLinkAsync();
                }

                await Task.WhenAll(
                    PingAsync(),
                    tick % WifiReadEverySeconds == 0 ? ReadWifiAsync() : Task.CompletedTask,
                    tick % WlanReadEverySeconds == 0 ? ReadWlanReasonsAsync() : Task.CompletedTask,
                    tick % DnsProbeEverySeconds == 0 ? ProbeDnsAsync() : Task.CompletedTask,
                    tick % NeighborScanEverySeconds == 0 ? ScanNeighborsAsync() : Task.CompletedTask
                );

                PublishStatus();
                _isFailing = false;
            }
            catch (Exception exception)
            {
                // One Event Per Failure Streak, Never One Per Second
                if (!_isFailing)
                {
                    _isFailing = true;
                    await _journal.TryRecordAsync(
                        EventKind.MonitorFailed,
                        EventSeverity.Critical,
                        $"Monitor Failed {exception.GetType().Name}"
                    );
                }
            }
        }
    }

    private void StartScheduledJobs(int tickCount)
    {
        // Daily Jobs Run Once The Quiet Window Has Passed
        if (tickCount >= 60 && DateTime.Today != _dailyJobsDate)
        {
            _dailyJobsDate = DateTime.Today;
            _ = RunDailyJobsAsync();
        }

        if (
            Settings.NightlySpeedTest
            && DateTime.Now.Hour == NightlySpeedTestHour
            && _speedTestDate != DateTime.Today
        )
        {
            _speedTestDate = DateTime.Today;
            _ = RunSpeedTestAsync();
        }
    }

    private async Task RecordStartedAsync()
    {
        // Explain The Gap Since The Last Data
        await using var context = new WifiDbContext();
        var lastDataUtc = await context.MinuteSamples.MaxAsync(sample =>
            (DateTime?)sample.MinuteUtc
        );
        var bootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var start = _isStartedAtSignIn ? "Started At Sign In" : "Started";
        var gap = lastDataUtc is { } last
            ? $", Off Since {Formatter.FormatLocal(last, "yyyy-MM-dd HH:mm")} ({Formatter.FormatDuration(DateTime.UtcNow - last)})"
            : ", First Run";
        await _journal.RecordAsync(
            EventKind.Started,
            EventSeverity.Info,
            $"{start}{gap}, PC Booted {Formatter.FormatLocal(bootUtc, "yyyy-MM-dd HH:mm")}"
        );
    }

    private async Task CloseMinuteAsync(DateTime minuteUtc)
    {
        var lastReading = _minuteReadings.LastOrDefault();
        var (routerPing, routerJitter, routerLoss) = Summarize(_routerRoundTrips);
        var (internetPing, internetJitter, internetLoss) = Summarize(_internetRoundTrips);
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
            RouterPingMilliseconds = routerPing,
            RouterJitterMilliseconds = routerJitter,
            RouterLossPercent = routerLoss,
            InternetPingMilliseconds = internetPing,
            InternetJitterMilliseconds = internetJitter,
            InternetLossPercent = internetLoss,
            DnsMilliseconds = Median(_dnsTimes),
            ReferenceDnsMilliseconds = Median(_referenceDnsTimes),
        };
        _minuteReadings.Clear();
        _routerRoundTrips.Clear();
        _internetRoundTrips.Clear();
        _dnsTimes.Clear();
        _referenceDnsTimes.Clear();

        await using (var context = new WifiDbContext())
        {
            context.MinuteSamples.Add(sample);
            await context.SaveChangesAsync();
        }

        // Startup Minutes Are Cold, Speed Tests Load The Line
        if (
            _journal.IsQuiet
            || _isSpeedTesting
            || DateTime.UtcNow - _speedTestEndedUtc < TimeSpan.FromMinutes(1)
        )
            return;

        await EvaluateConditionsAsync(sample, minuteUtc);
    }

    private async Task EvaluateConditionsAsync(MinuteSample sample, DateTime minuteUtc)
    {
        var settings = Settings;
        var localScope = sample.Link == EthernetLink ? HomeNetworkScope : WifiScope;
        var isOnWifi = sample.Link == WifiLink;
        var isOnline = sample.Link != OfflineLink;
        var isRouterFine = sample.RouterPingMilliseconds <= settings.RouterPingMilliseconds;
        var context = DescribeContext(sample);

        ConditionTracker.Check[] checks =
        [
            new(
                "WeakSignal",
                EventKind.WeakSignal,
                "Weak Signal",
                "dBm",
                isOnWifi ? sample.Rssi : null,
                settings.WeakSignalRssi,
                false,
                false,
                WifiScope,
                context,
                minuteUtc
            ),
            new(
                "LocalPing",
                EventKind.PingSpike,
                $"Ping Spike On {localScope}",
                "ms",
                sample.RouterPingMilliseconds,
                settings.RouterPingMilliseconds,
                true,
                false,
                localScope,
                context,
                minuteUtc
            ),
            new(
                "ProviderPing",
                EventKind.PingSpike,
                "Ping Spike At Internet Provider",
                "ms",
                isRouterFine ? sample.InternetPingMilliseconds : null,
                settings.InternetPingMilliseconds,
                true,
                false,
                ProviderScope,
                context,
                minuteUtc
            ),
            new(
                "LocalLoss",
                EventKind.PacketLoss,
                $"Packet Loss On {localScope}",
                "%",
                isOnline ? sample.RouterLossPercent : null,
                PacketLossPercent,
                true,
                true,
                localScope,
                context,
                minuteUtc
            ),
            new(
                "ProviderLoss",
                EventKind.PacketLoss,
                "Packet Loss At Internet Provider",
                "%",
                isOnline
                && sample.RouterLossPercent < PacketLossPercent
                && !_journal.IsOpen(WanDownKey)
                    ? sample.InternetLossPercent
                    : null,
                PacketLossPercent,
                true,
                true,
                ProviderScope,
                context,
                minuteUtc
            ),
            new(
                "SlowDns",
                EventKind.SlowDns,
                "Slow DNS",
                "ms",
                sample.DnsMilliseconds,
                SlowDnsMilliseconds,
                true,
                false,
                DnsScope,
                $"{context}, Reference 1.1.1.1 {Formatter.FormatNumber(sample.ReferenceDnsMilliseconds, "ms")}",
                minuteUtc
            ),
        ];

        foreach (var check in checks)
        {
            await _conditions.EvaluateAsync(check);
        }
    }

    private string DescribeContext(MinuteSample sample)
    {
        List<string> parts = [$"Link {sample.Link}"];
        if (sample.Rssi is { } rssi)
        {
            parts.Add($"Signal {rssi} dBm");
        }

        if (sample.ReceiveRateMbps is { } rate)
        {
            parts.Add($"Rate {rate} Mbps");
        }

        if (sample.Channel is { } channel)
        {
            parts.Add($"Channel {channel}{(WifiChannels.IsDfs(channel) ? " DFS" : string.Empty)}");
        }

        if (_ownAirtimePercent is { } airtime)
        {
            parts.Add($"Airtime Busy {airtime}%");
        }

        return string.Join(", ", parts);
    }

    private void PublishStatus()
    {
        Status = new(
            _link ?? OfflineLink,
            _routerAddress,
            _routerAdminUrl,
            _lastReading,
            _lastRouterRoundTrip,
            _lastInternetRoundTrip,
            _dfsFreeAtUtc > DateTime.UtcNow ? _dfsFreeAtUtc : null,
            _isSpeedTesting
        );
        StatusChanged?.Invoke();
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

        if (_routerAddress is { } routerAddress && !routerAddress.Equals(_adminRouterAddress))
        {
            _adminRouterAddress = routerAddress;
            _ = DetectRouterAdminAsync(routerAddress);
        }

        if (_link is not null && link != _link)
        {
            await TrackLinkChangeAsync(_link, link);
        }

        _link = link;
    }

    private async Task TrackLinkChangeAsync(string previousLink, string link)
    {
        await _journal.RecordAsync(
            EventKind.LinkChange,
            EventSeverity.Info,
            $"Switched From {previousLink} To {link}"
        );

        // A Wired PC Has No Wi-Fi Reading To Notice Drops
        if (previousLink == EthernetLink && link == OfflineLink)
        {
            await _journal.OpenAsync(
                WiredOfflineKey,
                EventKind.Disconnect,
                EventSeverity.Critical,
                "Ethernet Lost, PC Offline",
                HomeNetworkScope,
                null
            );
        }
        else if (_journal.OpenedAtUtc(WiredOfflineKey) is { } offlineSinceUtc)
        {
            await _journal.CloseAsync(
                WiredOfflineKey,
                $"Offline For {Formatter.FormatDuration(DateTime.UtcNow - offlineSinceUtc)}, Back On {link}"
            );
        }
    }

    private async Task DetectRouterAdminAsync(IPAddress routerAddress) =>
        _routerAdminUrl = await RouterAdmin.DetectAsync(routerAddress);

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
            if (_journal.OpenedAtUtc(WanDownKey) is { } wanDownSinceUtc)
            {
                await _journal.CloseAsync(
                    WanDownKey,
                    $"Internet Down For {Formatter.FormatDuration(DateTime.UtcNow - wanDownSinceUtc)}, Router Kept Answering"
                );
            }
        }
        else if (_lastRouterRoundTrip is not null && ++_internetFailures == WanDownAfterFailures)
        {
            // Router Answering While Internet Fails Points Past The House
            await _journal.OpenAsync(
                WanDownKey,
                EventKind.WanDown,
                EventSeverity.Critical,
                "Internet Down, Router Still Answers",
                ProviderScope,
                null,
                DateTime.UtcNow.AddSeconds(-WanDownAfterFailures)
            );
            await TraceIncidentAsync(WanDownKey);
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
        var reading = Settings.ReadWifiNatively
            ? NativeWifiReader.Read()
            : await WifiReader.ReadAsync();
        if (reading.IsBlocked && !_journal.IsOpen(LocationKey))
        {
            await _journal.OpenAsync(
                LocationKey,
                EventKind.LocationBlocked,
                EventSeverity.Critical,
                "Location Access Off So Wi-Fi Details Are Hidden",
                WifiScope,
                null
            );
        }
        else if (!reading.IsBlocked && _journal.OpenedAtUtc(LocationKey) is { } blockedSinceUtc)
        {
            await _journal.CloseAsync(
                LocationKey,
                $"Location Access Was Off For {Formatter.FormatDuration(DateTime.UtcNow - blockedSinceUtc)}"
            );
        }

        if (
            !reading.IsBlocked
            && _lastReading is { IsBlocked: false } previous
            && previous.IsConnected != reading.IsConnected
        )
        {
            await TrackConnectionAsync(previous, reading);
        }

        if (reading.IsConnected)
        {
            _minuteReadings.Add(reading);

            // Compared With The Last Connection So Drops Still Count
            if (_lastConnected is { } lastConnected)
            {
                await TrackRadioChangeAsync(lastConnected, reading);
            }

            await WarnWeatherRadarAsync(reading);
            _lastConnected = reading;
        }

        _lastReading = reading;
    }

    private async Task TrackConnectionAsync(WifiReading previous, WifiReading current)
    {
        if (previous.IsConnected)
        {
            var isOnEthernet = _link == EthernetLink;
            await _journal.OpenAsync(
                WifiDisconnectKey,
                EventKind.Disconnect,
                isOnEthernet ? EventSeverity.Info : EventSeverity.Critical,
                isOnEthernet
                    ? $"Wi-Fi Lost On {previous.Ssid}, Ethernet Still Up"
                    : $"Wi-Fi Lost On {previous.Ssid}",
                WifiScope,
                null
            );
            return;
        }

        if (_journal.OpenedAtUtc(WifiDisconnectKey) is not { } lostAtUtc)
            return;

        // Reconnecting Onto A New DFS Channel Waited Out Radar Check
        var isDfsCheck = current.IsDfs && current.Channel != _lastConnected?.Channel;
        var check = isDfsCheck
            ? ", Router Was Checking The New DFS Channel For Radar"
            : string.Empty;
        await _journal.CloseAsync(
            WifiDisconnectKey,
            $"Wi-Fi Lost For {Formatter.FormatDuration(DateTime.UtcNow - lostAtUtc)}, Back On {current.Ssid} Channel {current.Channel}{check}"
        );
    }

    private async Task TrackRadioChangeAsync(WifiReading previous, WifiReading current)
    {
        if (previous.Band != current.Band)
        {
            await _journal.RecordAsync(
                EventKind.BandChange,
                current.Band == FiveGigahertz ? EventSeverity.Info : EventSeverity.Warning,
                $"Band {previous.Band} To {current.Band} On {current.Ssid}",
                WifiScope
            );
            return;
        }

        if (previous.Channel == current.Channel)
            return;

        var (kind, message) = WifiReader.DescribeChannelChange(previous.Channel, current.Channel);
        switch (kind)
        {
            case EventKind.DfsEviction:
                _evictedAtUtc = DateTime.UtcNow;
                _dfsFreeAtUtc = DateTime.UtcNow + WifiChannels.NonOccupancy;
                await _journal.RecordAsync(
                    kind,
                    EventSeverity.Critical,
                    $"{message}, Radar Heard On {previous.Channel}, Router May Return From {Formatter.FormatLocal(_dfsFreeAtUtc.Value, "HH:mm")}",
                    WifiScope
                );
                break;
            case EventKind.DfsReturn:
                var away = _evictedAtUtc is { } evictedAtUtc
                    ? $" After {Formatter.FormatDuration(DateTime.UtcNow - evictedAtUtc)} Away"
                    : string.Empty;
                await _journal.RecordAsync(kind, EventSeverity.Info, $"{message}{away}", WifiScope);
                break;
            default:
                await _journal.RecordAsync(kind, EventSeverity.Warning, message, WifiScope);
                break;
        }
    }

    private async Task WarnWeatherRadarAsync(WifiReading reading)
    {
        if (_hasWarnedWeatherRadar || !WifiChannels.IsWeatherRadar(reading.Channel))
            return;

        _hasWarnedWeatherRadar = true;
        await _journal.RecordAsync(
            EventKind.ChannelChange,
            EventSeverity.Warning,
            $"Channel {reading.Channel} Shares The Weather Radar Band, Every Move Onto It Waits {WifiChannels.WeatherChannelCheck.TotalMinutes:0} Minutes Silent",
            WifiScope
        );
    }

    private int? AverageReading(Func<WifiReading, int?> selector) =>
        _minuteReadings.Average(selector) is { } average ? (int)Math.Round(average) : null;
    #endregion

    #region Diagnosis Methods
    private async Task ReadWlanReasonsAsync()
    {
        var notices = await Task.Run(() => WlanEventReader.ReadAfter(_wlanRecordId));
        foreach (var notice in notices)
        {
            _wlanRecordId = Math.Max(_wlanRecordId, notice.RecordId);
            switch (notice.EventId)
            {
                case WlanEventReader.DisconnectedEvent:
                    await AttachDisconnectReasonAsync(notice);
                    break;
                case WlanEventReader.ConnectFailedEvent:
                    _connectFailures++;
                    await _journal.OpenAsync(
                        ConnectFailedKey,
                        EventKind.ConnectFailed,
                        EventSeverity.Warning,
                        $"Could Not Connect To {notice.Ssid}",
                        WifiScope,
                        new EventDetails(Reason: notice.Reason)
                    );
                    await _journal.UpdateAsync(
                        ConnectFailedKey,
                        $"Could Not Connect To {notice.Ssid}, {_connectFailures} Attempts",
                        changeDetails: details => details with { Reason = notice.Reason }
                    );
                    break;
                case WlanEventReader.ConnectedEvent
                    when _journal.OpenedAtUtc(ConnectFailedKey) is { } failingSinceUtc:
                    await _journal.CloseAsync(
                        ConnectFailedKey,
                        $"Could Not Connect To {notice.Ssid} For {Formatter.FormatDuration(notice.TimeUtc - failingSinceUtc)}, {_connectFailures} Attempts, Then Connected"
                    );
                    _connectFailures = 0;
                    break;
            }
        }
    }

    private async Task AttachDisconnectReasonAsync(WlanNotice notice)
    {
        EventDetails WithReason(EventDetails details) => details with { Reason = notice.Reason };

        if (_journal.IsOpen(WifiDisconnectKey))
        {
            await _journal.UpdateAsync(WifiDisconnectKey, changeDetails: WithReason);
        }
        else if (
            !await _journal.AttachToLatestAsync(EventKind.Disconnect, s_reasonWindow, WithReason)
        )
        {
            // Faster Than A Wi-Fi Reading, Only Windows Saw It
            await _journal.RecordAsync(
                EventKind.Disconnect,
                EventSeverity.Warning,
                $"Wi-Fi Dropped Briefly On {notice.Ssid}",
                WifiScope,
                new EventDetails(Reason: notice.Reason)
            );
        }
    }

    private Task TraceIncidentAsync(string key)
    {
        // Outages Always Trace, Smaller Incidents At Most Every Few Minutes
        if (key != WanDownKey && DateTime.UtcNow - _lastTraceUtc < s_traceSpacing)
            return Task.CompletedTask;

        _lastTraceUtc = DateTime.UtcNow;
        var routerAddress = _routerAddress;
        _ = Task.Run(async () =>
        {
            try
            {
                var (summary, hops) = await FaultTracer.TraceAsync(
                    s_internetAddress,
                    routerAddress
                );
                await _journal.UpdateAsync(
                    key,
                    changeDetails: details => details with { TraceSummary = summary, Trace = hops }
                );
            }
            catch
            {
                // A Failed Trace Only Means Less Detail
            }
        });
        return Task.CompletedTask;
    }

    private async Task ProbeDnsAsync()
    {
        // A Cold First Query Reads Hundreds Of Milliseconds
        if (_journal.IsQuiet)
            return;

        var systemServer = DnsProbe.SystemServer();
        var systemTask = systemServer is null
            ? Task.FromResult<double?>(null)
            : DnsProbe.MeasureAsync(systemServer);
        var referenceTask = DnsProbe.MeasureAsync(DnsProbe.ReferenceServer);
        var systemTime = await systemTask;
        var referenceTime = await referenceTask;

        // A Silent Resolver Counts As A Timeout While 1.1.1.1 Answers
        _dnsTimes.Add(
            systemTime
                ?? (
                    systemServer is not null && referenceTime is not null
                        ? DnsProbe.TimeoutMilliseconds
                        : null
                )
        );
        _referenceDnsTimes.Add(referenceTime);
    }

    private async Task ScanNeighborsAsync()
    {
        var neighbors = await WifiReader.ReadNeighborsAsync();
        if (neighbors.Count == 0)
            return;

        var scanUtc = DateTime.UtcNow;
        var ownSsid = _lastConnected?.Ssid;
        _ownAirtimePercent =
            neighbors
                .FirstOrDefault(neighbor => neighbor.Ssid == ownSsid)
                ?.ChannelUtilizationPercent
            ?? _ownAirtimePercent;
        await using var context = new WifiDbContext();
        context.NeighborSamples.AddRange(
            neighbors.Select(neighbor => new NeighborSample
            {
                ScanUtc = scanUtc,
                Ssid = neighbor.Ssid,
                Bssid = neighbor.Bssid,
                Channel = neighbor.Channel,
                SignalPercent = neighbor.SignalPercent,
                ChannelUtilizationPercent = neighbor.ChannelUtilizationPercent,
                IsOwn = neighbor.Ssid == ownSsid,
            })
        );
        await context.SaveChangesAsync();
    }

    private async Task RunDailyJobsAsync()
    {
        try
        {
            await SummaryWriter.WriteMissingAsync(_journal, Settings.DailySummaryNotification);
            await InspectAdapterAsync();
            await CheckForUpdateAsync();
        }
        catch
        {
            // A Failed Daily Job Tries Again Tomorrow
        }
    }

    public async Task InspectAdapterAsync()
    {
        Adapter = await AdapterInspector.InspectAsync();
        if (Adapter is not { IsDriverOld: true, DriverVersion: { } version })
            return;

        // Each Old Driver Version Is Mentioned Once
        var message =
            $"Wi-Fi Driver {version} Dates From {Adapter.DriverDate:yyyy-MM-dd}, Check Windows Update Or The Maker's Site";
        if (
            !await EventJournal.HasMessageStartingWithAsync(
                EventKind.DriverCheck,
                $"Wi-Fi Driver {version} "
            )
        )
        {
            await _journal.RecordAsync(
                EventKind.DriverCheck,
                EventSeverity.Warning,
                message,
                WifiScope,
                isAlwaysAlerted: true
            );
        }
    }

    public Task RecordFailureAsync(string message) =>
        _journal.TryRecordAsync(EventKind.MonitorFailed, EventSeverity.Critical, message);

    public async Task<Version?> CheckForUpdateAsync()
    {
        // Announce Each New Version Once
        var latest = await UpdateChecker.CheckAsync();
        if (latest is null || latest == AvailableUpdate)
            return latest;

        AvailableUpdate = latest;
        await _journal.RecordAsync(
            EventKind.UpdateAvailable,
            EventSeverity.Info,
            $"Version {latest.ToString(2)} Is Available"
        );
        UpdateFound?.Invoke();
        return latest;
    }

    public async Task<SpeedTest?> RunSpeedTestAsync(
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_isSpeedTesting)
            return null;

        _isSpeedTesting = true;
        PublishStatus();
        try
        {
            // Off The UI Thread, Or Every Read Queues On It
            var result = await Task.Run(
                () => SpeedTester.RunAsync(_link ?? OfflineLink, progress, cancellationToken),
                cancellationToken
            );
            await using (var context = new WifiDbContext())
            {
                context.SpeedTests.Add(result);
                await context.SaveChangesAsync(CancellationToken.None);
            }

            var addedLatency =
                Math.Max(result.DownloadPingMilliseconds ?? 0, result.UploadPingMilliseconds ?? 0)
                - (result.IdlePingMilliseconds ?? 0);
            await _journal.RecordAsync(
                EventKind.SpeedTest,
                EventSeverity.Info,
                $"Speed Test {result.DownloadMbps:0} Mbps Down, {result.UploadMbps:0} Mbps Up, Ping Under Load +{Math.Max(0, addedLatency):0} ms, Grade {result.Grade}"
            );
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A Cancelled Test Is Not A Failure
            return null;
        }
        catch (Exception exception)
        {
            await _journal.TryRecordAsync(
                EventKind.SpeedTest,
                EventSeverity.Warning,
                $"Speed Test Failed {exception.GetType().Name}"
            );
            return null;
        }
        finally
        {
            _isSpeedTesting = false;
            _speedTestEndedUtc = DateTime.UtcNow;
            PublishStatus();
        }
    }
    #endregion

    #region Math Methods
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

    private static double? Average(List<double?> values) =>
        values.Average() is { } average ? Math.Round(average, 1) : null;

    private static double? Median(List<double?> values)
    {
        // One Lost Lookup Of Four Is Ignored, Two Count
        var sorted = values.OfType<double>().Order().ToList();
        return sorted.Count == 0 ? null : Math.Round(sorted[sorted.Count / 2], 1);
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);
    #endregion
}
