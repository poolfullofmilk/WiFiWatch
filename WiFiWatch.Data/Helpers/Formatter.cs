using System.Globalization;
using WiFiWatch.Data.Models;

namespace WiFiWatch.Data.Helpers;

public static class Formatter
{
    public static string FormatDuration(TimeSpan duration)
    {
        // The Two Largest Parts, Seconds Only Under A Minute
        return duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration.Hours}h"
            : duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{Math.Max(0, duration.Seconds)}s";
    }

    public static string? FormatIncidentLength(DateTime occurredAtUtc, DateTime? endedAtUtc)
    {
        // Instants Have No Length, Open Ones Carry No Stale Clock
        return endedAtUtc is not { } endUtc ? "Ongoing"
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
            ? $"+{Math.Max(0, loadedPing - idlePing):0} ms"
            : "-";
    }

    public static string FormatLocal(DateTime utcTime, string format) =>
        utcTime.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);

    public static string FormatWhen(DateTime utcTime)
    {
        // Recent Days Read As Words
        var localTime = utcTime.ToLocalTime();
        var dayLabel =
            localTime.Date == DateTime.Today ? "Today"
            : localTime.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
            : localTime.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        return $"{dayLabel} {localTime:HH:mm}";
    }

    public static string FormatNumber(double? value, string unit) =>
        value is null ? "-" : $"{value:0.#} {unit}";

    public static string FormatCount(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
