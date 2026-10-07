namespace WifiWatch.Data.ViewModels;

public sealed record TraceHop(
    int Number,
    string? Address,
    string? Name,
    double LossPercent,
    double? AverageMilliseconds
);
