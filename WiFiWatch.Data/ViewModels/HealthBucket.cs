using WiFiWatch.Data.Enums;

namespace WiFiWatch.Data.ViewModels;

public sealed record HealthBucket(
    DateTime StartUtc,
    DateTime EndUtc,
    HealthState State,
    int ProblemCount,
    double? RouterPing,
    double? InternetPing,
    double? DnsPing
);
