namespace WifiWatch.Data.ViewModels;

public sealed record PeriodSummary(
    int MonitoredMinutes,
    double OnlinePercent,
    int IncidentCount,
    TimeSpan LongestOutage,
    int EvictionCount,
    double? DfsPercent,
    int? WorstHour,
    double? WorstHourPing
);
