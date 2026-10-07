namespace WifiWatch.Data.ViewModels;

public sealed record WlanNotice(
    long RecordId,
    DateTime TimeUtc,
    int EventId,
    string? Ssid,
    string? Reason
);
