using System.Diagnostics;
using System.Globalization;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;

namespace WiFiWatch.Data.Helpers;

public static class Formatter
{
    static Formatter() => Debug.Assert(SelfTestPasses());

    public static string FormatDuration(TimeSpan duration) =>
        FormatDuration(duration, AppText.Culture);

    private static string FormatDuration(TimeSpan duration, CultureInfo culture)
    {
        var (whole, unit, part, partUnit) =
            duration.TotalDays >= 1
                ? ((int)duration.TotalDays, "DurationDay", duration.Hours, "DurationHour")
            : duration.TotalHours >= 1
                ? ((int)duration.TotalHours, "DurationHour", duration.Minutes, "DurationMinute")
            : duration.TotalMinutes >= 1
                ? (duration.Minutes, "DurationMinute", duration.Seconds, "DurationSecond")
            : (Math.Max(0, duration.Seconds), "DurationSecond", 0, "DurationSecond");
        var first = AppText.Format(culture, unit, whole);
        return part == 0 ? first : $"{first} {AppText.Format(culture, partUnit, part)}";
    }

    public static string? FormatIncidentLength(DateTime occurredAtUtc, DateTime? endedAtUtc)
    {
        // Instants Have No Length, Open Ones Carry No Stale Clock
        return endedAtUtc is not { } endUtc ? AppText.Get("Ongoing")
            : endUtc == occurredAtUtc ? null
            : FormatDuration(endUtc - occurredAtUtc);
    }

    public static string FormatLagUnderLoad(SpeedTest test)
    {
        // The Worse Direction Shows How Much A Busy Line Lags
        var loadedPing = Math.Max(
            test.DownloadPingMilliseconds ?? 0,
            test.UploadPingMilliseconds ?? 0
        );
        return test.IdlePingMilliseconds is { } idlePing
            ? $"+{Math.Max(0, loadedPing - idlePing):0} {AppText.Get("ms")}"
            : "-";
    }

    public static string FormatLocal(DateTime utcTime, string format) =>
        utcTime
            .ToLocalTime()
            .ToString(
                format,
                format.Contains("MMM") || format.Contains("ddd")
                    ? AppText.Culture
                    : CultureInfo.CurrentCulture
            );

    public static string FormatWhen(DateTime utcTime)
    {
        // Recent Days Read As Words
        var localTime = utcTime.ToLocalTime();
        var dayLabel =
            localTime.Date == DateTime.Today ? AppText.Get("Today")
            : localTime.Date == DateTime.Today.AddDays(-1) ? AppText.Get("Yesterday")
            : localTime.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        return $"{dayLabel} {localTime:HH:mm}";
    }

    public static string FormatNumber(double? value, string unit) =>
        value is null ? "-" : $"{value:0.#} {AppText.Get(unit)}";

    public static string FormatCount(int count, string noun) => AppText.FormatCount(count, noun);

    private static bool SelfTestPasses() =>
        FormatDuration(TimeSpan.FromMinutes(2), CultureInfo.GetCultureInfo("en")) == "2m"
        && FormatDuration(TimeSpan.FromSeconds(125), CultureInfo.GetCultureInfo("en")) == "2m 5s"
        && FormatDuration(TimeSpan.FromMinutes(60), CultureInfo.GetCultureInfo("en")) == "1h"
        && FormatDuration(TimeSpan.FromHours(26), CultureInfo.GetCultureInfo("en")) == "1d 2h"
        && FormatDuration(TimeSpan.FromSeconds(25), CultureInfo.GetCultureInfo("en")) == "25s"
        && FormatDuration(TimeSpan.FromSeconds(-3), CultureInfo.GetCultureInfo("en")) == "0s";
}
