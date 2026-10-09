namespace WiFiWatch.Data.ViewModels;

public sealed record NeighborReading(
    string Ssid,
    string Bssid,
    int Channel,
    int SignalPercent,
    int? ChannelUtilizationPercent
);
