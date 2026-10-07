namespace WifiWatch.Data.ViewModels;

public sealed record ChartPoint(string Label, decimal? Value, DateTime? StartUtc = null);
