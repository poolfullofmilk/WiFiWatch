using Microsoft.EntityFrameworkCore;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static class SummaryWriter
{
    public static async Task<List<HourSummary>> LoadHoursAsync(
        WifiDbContext context,
        DateTime startUtc,
        DateTime endUtc
    )
    {
        // Ponytail: UTC Hours, Exact Only For Whole Hour Time Zones
        var hours = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .GroupBy(sample => new { sample.MinuteUtc.Date, sample.MinuteUtc.Hour })
            .Select(hour => new
            {
                hour.Key.Date,
                hour.Key.Hour,
                MinuteCount = hour.Count(),
                OnlineMinutes = hour.Count(sample =>
                    sample.Link != NetworkMonitor.OfflineLink && sample.InternetLossPercent < 100
                ),
                ChannelMinutes = hour.Count(sample => sample.Channel != null),

                // Same Range As WifiChannels.IsDfs, Written Out For SQL
                DfsMinutes = hour.Count(sample => sample.Channel >= 52 && sample.Channel <= 144),
                RouterPing = hour.Average(sample => sample.RouterPingMilliseconds),
                RouterCount = hour.Count(sample => sample.RouterPingMilliseconds != null),
                InternetPing = hour.Average(sample => sample.InternetPingMilliseconds),
                InternetCount = hour.Count(sample => sample.InternetPingMilliseconds != null),
                DnsPing = hour.Average(sample => sample.DnsMilliseconds),
                DnsCount = hour.Count(sample => sample.DnsMilliseconds != null),
            })
            .ToListAsync();

        return
        [
            .. hours
                .Select(hour => new HourSummary(
                    DateTime.SpecifyKind(hour.Date.AddHours(hour.Hour), DateTimeKind.Utc),
                    hour.MinuteCount,
                    hour.OnlineMinutes,
                    hour.ChannelMinutes,
                    hour.DfsMinutes,
                    hour.RouterPing,
                    hour.RouterCount,
                    hour.InternetPing,
                    hour.InternetCount,
                    hour.DnsPing,
                    hour.DnsCount
                ))
                .OrderBy(hour => hour.HourUtc),
        ];
    }

    public static async Task<PeriodSummary> SummarizeAsync(DateTime startUtc, DateTime endUtc)
    {
        await using var context = new WifiDbContext();
        var hours = await LoadHoursAsync(context, startUtc, endUtc);
        return await SummarizeAsync(context, hours, startUtc, endUtc);
    }

    public static async Task<PeriodSummary> SummarizeAsync(
        WifiDbContext context,
        List<HourSummary> hours,
        DateTime startUtc,
        DateTime endUtc
    )
    {
        var events = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc && wifiEvent.OccurredAtUtc < endUtc
            )
            .Select(wifiEvent => new
            {
                wifiEvent.Kind,
                wifiEvent.Severity,
                wifiEvent.OccurredAtUtc,
                wifiEvent.EndedAtUtc,
            })
            .ToListAsync();
        var problemCount = await context
            .Events.Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc && wifiEvent.OccurredAtUtc < endUtc
            )
            .Where(Problems.IsProblem)
            .CountAsync();

        var minuteCount = hours.Sum(hour => hour.MinuteCount);
        var onlineMinutes = hours.Sum(hour => hour.OnlineMinutes);
        var channelMinutes = hours.Sum(hour => hour.ChannelMinutes);
        var (worstHour, worstHourPing) = hours
            .GroupBy(hour => hour.HourUtc.ToLocalTime().Hour)
            .Select(hourOfDay =>
                (
                    Hour: hourOfDay.Key,
                    Ping: HealthBuckets.Average(
                        hourOfDay,
                        hour => (hour.InternetPing, hour.InternetCount)
                    ) ?? 0
                )
            )
            .OrderByDescending(hourOfDay => hourOfDay.Ping)
            .FirstOrDefault();

        return new(
            minuteCount,
            minuteCount == 0 ? 0 : Math.Round(100.0 * onlineMinutes / minuteCount, 1),
            problemCount,
            events
                .Where(wifiEvent =>
                    // Wi-Fi Lost While A Cable Holds Is No Outage
                    wifiEvent.Kind
                        is EventKind.WanDown
                            or EventKind.Disconnect
                    && wifiEvent.Severity != EventSeverity.Info
                    && wifiEvent.EndedAtUtc is not null
                )
                .Select(wifiEvent => wifiEvent.EndedAtUtc!.Value - wifiEvent.OccurredAtUtc)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max(),
            events.Count(wifiEvent => wifiEvent.Kind == EventKind.DfsEviction),
            channelMinutes == 0
                ? null
                : Math.Round(100.0 * hours.Sum(hour => hour.DfsMinutes) / channelMinutes),
            worstHourPing > 0 ? worstHour : null,
            worstHourPing > 0 ? Math.Round(worstHourPing, 1) : null
        );
    }

    public static string Describe(string label, PeriodSummary summary)
    {
        List<string> parts =
        [
            $"{summary.OnlinePercent:0.#}% Online",
            Formatter.FormatCount(summary.ProblemCount, "Problem"),
            summary.LongestOutage > TimeSpan.Zero
                ? $"Longest Outage {Formatter.FormatDuration(summary.LongestOutage)}"
                : "No Outages",
            Formatter.FormatCount(summary.EvictionCount, "DFS Eviction"),
        ];
        if (summary.DfsPercent is { } dfsPercent)
        {
            parts.Add($"{dfsPercent:0}% On DFS");
        }

        if (summary.WorstHour is { } worstHour)
        {
            parts.Add($"Worst Hour {worstHour:00}h At {summary.WorstHourPing:0.#} ms");
        }

        return $"{label}: {string.Join(", ", parts)}";
    }

    public static async Task WriteMissingAsync(EventJournal journal, bool isNotified)
    {
        var yesterday = DateTime.Today.AddDays(-1);
        var label = $"{yesterday:yyyy-MM-dd} {yesterday:dddd}";
        if (await EventJournal.HasMessageStartingWithAsync(EventKind.DailySummary, label))
            return;

        var summary = await SummarizeAsync(
            yesterday.ToUniversalTime(),
            DateTime.Today.ToUniversalTime()
        );
        if (summary.MonitoredMinutes == 0)
            return;

        // A Fine Day Is Logged Quietly, A Bad One Notifies
        var hadProblems = summary.ProblemCount > 0 || summary.LongestOutage > TimeSpan.Zero;
        await journal.RecordAsync(
            EventKind.DailySummary,
            EventSeverity.Info,
            Describe(label, summary),
            isAlwaysAlerted: isNotified && hadProblems
        );
    }
}
