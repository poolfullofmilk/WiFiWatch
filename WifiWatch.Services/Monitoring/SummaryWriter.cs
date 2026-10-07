using Microsoft.EntityFrameworkCore;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static class SummaryWriter
{
    public static async Task<PeriodSummary> SummarizeAsync(DateTime startUtc, DateTime endUtc)
    {
        await using var context = new WifiDbContext();
        var minutes = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .Select(sample => new
            {
                sample.MinuteUtc,
                sample.Link,
                sample.Channel,
                sample.InternetLossPercent,
                sample.InternetPingMilliseconds,
            })
            .ToListAsync();
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

        var onlineMinutes = minutes.Count(minute =>
            minute.Link != NetworkMonitor.OfflineLink && minute.InternetLossPercent < 100
        );
        var channelMinutes = minutes.Where(minute => minute.Channel is not null).ToList();
        var (worstHour, worstHourPing) = minutes
            .Where(minute => minute.InternetPingMilliseconds is not null)
            .GroupBy(minute => minute.MinuteUtc.ToLocalTime().Hour)
            .Select(hour =>
                (
                    Hour: hour.Key,
                    Ping: hour.Average(minute => minute.InternetPingMilliseconds!.Value)
                )
            )
            .OrderByDescending(hour => hour.Ping)
            .FirstOrDefault();

        return new(
            minutes.Count,
            minutes.Count == 0 ? 0 : Math.Round(100.0 * onlineMinutes / minutes.Count, 1),
            events.Count(wifiEvent => wifiEvent.Severity != EventSeverity.Info),
            events
                .Where(wifiEvent =>
                    wifiEvent.Kind is EventKind.WanDown or EventKind.Disconnect
                    && wifiEvent.EndedAtUtc is not null
                )
                .Select(wifiEvent => wifiEvent.EndedAtUtc!.Value - wifiEvent.OccurredAtUtc)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max(),
            events.Count(wifiEvent => wifiEvent.Kind == EventKind.DfsEviction),
            channelMinutes.Count == 0
                ? null
                : Math.Round(
                    100.0
                        * channelMinutes.Count(minute => WifiChannels.IsDfs(minute.Channel))
                        / channelMinutes.Count
                ),
            worstHourPing > 0 ? worstHour : null,
            worstHourPing > 0 ? Math.Round(worstHourPing, 1) : null
        );
    }

    public static string Describe(string label, PeriodSummary summary)
    {
        List<string> parts =
        [
            $"{summary.OnlinePercent:0.#}% Online",
            $"{summary.IncidentCount} Incidents",
            summary.LongestOutage > TimeSpan.Zero
                ? $"Longest Outage {Formatter.FormatDuration(summary.LongestOutage)}"
                : "No Outages",
            $"{summary.EvictionCount} DFS Evictions",
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
        // Yesterday, Then Last Week Once A New Week Starts
        var yesterday = DateTime.Today.AddDays(-1);
        await WriteAsync(
            journal,
            EventKind.DailySummary,
            $"{yesterday:yyyy-MM-dd} {yesterday:dddd}",
            yesterday,
            DateTime.Today,
            isNotified
        );

        var thisWeekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        var lastWeekStart = thisWeekStart.AddDays(-7);
        await WriteAsync(
            journal,
            EventKind.WeeklySummary,
            $"Week Of {lastWeekStart:yyyy-MM-dd}",
            lastWeekStart,
            thisWeekStart,
            isNotified
        );
    }

    private static async Task WriteAsync(
        EventJournal journal,
        EventKind kind,
        string label,
        DateTime firstDay,
        DateTime endDay,
        bool isNotified
    )
    {
        if (await EventJournal.HasMessageStartingWithAsync(kind, label))
            return;

        var summary = await SummarizeAsync(firstDay.ToUniversalTime(), endDay.ToUniversalTime());
        if (summary.MonitoredMinutes == 0)
            return;

        await journal.RecordAsync(
            kind,
            EventSeverity.Info,
            Describe(label, summary),
            isAlwaysAlerted: isNotified
        );
    }
}
