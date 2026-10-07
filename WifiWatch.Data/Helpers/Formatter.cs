using System.Globalization;

namespace WifiWatch.Data.Helpers;

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

    public static string FormatLocal(DateTime utcTime, string format) =>
        utcTime.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);

    public static string FormatNumber(double? value, string unit) =>
        value is null ? "-" : $"{value:0.#} {unit}";
}
