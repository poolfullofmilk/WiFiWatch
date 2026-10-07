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
        IReadOnlyList<MinuteSample> samples,
        IReadOnlyList<WifiEvent> incidents,
        List<DateTime> startsUtc,
        DateTime endUtc,
        DateTime nowUtc
    )
    {
        var samplesByBucket = samples.ToLookup(sample =>
        {
            var index = startsUtc.BinarySearch(sample.MinuteUtc);
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
                    var minutes = samplesByBucket[bucket].ToList();
                    var state =
                        overlapping.Any(incident => incident.Severity == EventSeverity.Critical)
                            ? HealthState.Critical
                        : overlapping.Count > 0 ? HealthState.Warning
                        : minutes.Count == 0 ? HealthState.NoData
                        : HealthState.Healthy;

                    return new HealthBucket(
                        bucketStartUtc,
                        bucketEndUtc,
                        state,
                        overlapping.Count,
                        minutes.Average(sample => sample.RouterPingMilliseconds),
                        minutes.Average(sample => sample.InternetPingMilliseconds),
                        minutes.Average(sample => sample.DnsMilliseconds)
                    );
                }
            ),
        ];
    }

    private static bool SelfTestPasses()
    {
        var startUtc = new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);
        List<MinuteSample> samples =
        [
            new()
            {
                MinuteUtc = startUtc.AddMinutes(5),
                Link = "Wi-Fi",
                InternetPingMilliseconds = 8,
            },
            new()
            {
                MinuteUtc = startUtc.AddMinutes(70),
                Link = "Wi-Fi",
                InternetPingMilliseconds = 12,
            },
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

        List<DateTime> startsUtc =
        [
            .. Enumerable.Range(0, 5).Select(hour => startUtc.AddHours(hour)),
        ];
        var buckets = Build(
            samples,
            incidents,
            startsUtc,
            startUtc.AddHours(5),
            startUtc.AddMinutes(200)
        );
        return buckets[0].State == HealthState.Healthy
            && buckets[0].InternetPing == 8
            && buckets[1].State == HealthState.Warning
            && buckets[2].State == HealthState.Critical
            && buckets[3].State == HealthState.Critical
            && buckets[4].State == HealthState.NoData;
    }
}
