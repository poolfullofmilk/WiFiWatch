using System.Diagnostics;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Data.Helpers;

public static class HealthBuckets
{
    static HealthBuckets() => Debug.Assert(SelfTestPasses());

    public static List<DateTime> Starts(DateTime firstLocal, int count, bool isDaily)
    {
        // Days Start At Local Midnight, So Daylight Saving Never Shifts Them
        return
        [
            .. Enumerable
                .Range(0, count)
                .Select(index =>
                    isDaily
                        ? firstLocal.AddDays(index).ToUniversalTime()
                        : firstLocal.ToUniversalTime().AddHours(index)
                ),
        ];
    }

    public static List<HealthBucket> Build(
        IReadOnlyList<HourSummary> hours,
        IReadOnlyList<WifiEvent> incidents,
        List<DateTime> startsUtc,
        DateTime endUtc,
        DateTime nowUtc
    )
    {
        var hoursByBucket = hours.ToLookup(hour =>
        {
            var index = startsUtc.BinarySearch(hour.HourUtc);
            return index >= 0 ? index : ~index - 1;
        });
        var problems = incidents.Where(Problems.IsProblemInMemory).ToList();

        return
        [
            .. startsUtc.Select(
                (bucketStartUtc, bucket) =>
                {
                    var bucketEndUtc =
                        bucket + 1 < startsUtc.Count ? startsUtc[bucket + 1] : endUtc;

                    // Open Problems Run Until Now, Instants Touch One Bucket
                    var overlapping = problems
                        .Where(incident =>
                            incident.OccurredAtUtc < bucketEndUtc
                            && (incident.EndedAtUtc ?? nowUtc) >= bucketStartUtc
                        )
                        .ToList();
                    var bucketHours = hoursByBucket[bucket].ToList();
                    var state =
                        overlapping.Any(incident => incident.Severity == EventSeverity.Critical)
                            ? HealthState.Critical
                        : overlapping.Count > 0 ? HealthState.Warning
                        : bucketHours.Sum(hour => hour.MinuteCount) == 0 ? HealthState.NoData
                        : HealthState.Healthy;

                    return new HealthBucket(
                        bucketStartUtc,
                        bucketEndUtc,
                        state,
                        overlapping.Count,
                        Average(bucketHours, hour => (hour.RouterPing, hour.RouterCount)),
                        Average(bucketHours, hour => (hour.InternetPing, hour.InternetCount)),
                        Average(bucketHours, hour => (hour.DnsPing, hour.DnsCount))
                    );
                }
            ),
        ];
    }

    public static double? Average(
        IEnumerable<HourSummary> hours,
        Func<HourSummary, (double? Average, int Count)> selector
    )
    {
        // Each Hour Weighs As Much As Its Minutes
        var parts = hours
            .Select(selector)
            .Where(part => part.Average is not null && part.Count > 0)
            .ToList();
        var count = parts.Sum(part => part.Count);
        return count == 0 ? null : parts.Sum(part => part.Average!.Value * part.Count) / count;
    }

    private static bool SelfTestPasses()
    {
        var startUtc = new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);
        List<HourSummary> hours =
        [
            new(startUtc, 60, 60, 60, 60, 2, 60, 8, 60, null, 0),
            new(startUtc.AddHours(1), 20, 20, 20, 20, 3, 20, 12, 20, 5, 4),
        ];
        List<WifiEvent> incidents =
        [
            new()
            {
                OccurredAtUtc = startUtc.AddMinutes(80),
                EndedAtUtc = startUtc.AddMinutes(90),
                Severity = EventSeverity.Warning,
                Message = "Jitter",
            },
            new()
            {
                OccurredAtUtc = startUtc.AddMinutes(20),
                EndedAtUtc = startUtc.AddMinutes(22),
                Severity = EventSeverity.Warning,
                Message = "Short Jitter",
            },
            new()
            {
                OccurredAtUtc = startUtc.AddMinutes(150),
                Severity = EventSeverity.Critical,
                Message = "Internet Down",
            },
        ];

        List<DateTime> hourStartsUtc =
        [
            .. Enumerable.Range(0, 5).Select(hour => startUtc.AddHours(hour)),
        ];
        var hourly = Build(
            hours,
            incidents,
            hourStartsUtc,
            startUtc.AddHours(5),
            startUtc.AddMinutes(200)
        );
        var whole = Build(
            hours,
            incidents,
            [startUtc],
            startUtc.AddHours(5),
            startUtc.AddMinutes(200)
        );
        return hourly[0].State == HealthState.Healthy
            && hourly[0].InternetPing == 8
            && hourly[1].State == HealthState.Warning
            && hourly[2].State == HealthState.Critical
            && hourly[3].State == HealthState.Critical
            && hourly[4].State == HealthState.NoData
            && whole[0].InternetPing == 9
            && whole[0].DnsPing == 5;
    }
}
