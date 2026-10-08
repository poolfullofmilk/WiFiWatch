using WifiWatch.Data.Enums;

namespace WifiWatch.Data.ViewModels;

public sealed record SpeedTestProgress(
    SpeedTestPhase Phase,
    double PhaseFraction,
    double? CurrentMbps,
    double? IdlePing,
    double? DownloadMbps
);
