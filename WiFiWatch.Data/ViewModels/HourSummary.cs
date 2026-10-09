namespace WiFiWatch.Data.ViewModels;

public sealed record HourSummary(
    DateTime HourUtc,
    int MinuteCount,
    int OnlineMinutes,
    int ChannelMinutes,
    int DfsMinutes,
    double? RouterPing,
    int RouterCount,
    double? InternetPing,
    int InternetCount,
    double? DnsPing,
    int DnsCount
);
