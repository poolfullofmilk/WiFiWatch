using WiFiWatch.Data.Enums;

namespace WiFiWatch.Data.ViewModels;

public sealed record SpeedTestProgress(
    SpeedTestPhase Phase,
    double PhaseFraction,
    double? CurrentMbps,
    double? IdlePing,
    double? DownloadMbps
);
