using Microsoft.AspNetCore.Components;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class HealthStrip
{
    // Label Steps That Land On Round Hours And Days
    private const int MaximumLabels = 12;
    private static readonly int[] s_hourSteps = [1, 2, 3, 6, 12, 24];
    private static readonly int[] s_daySteps = [1, 2, 7, 14, 28];

    [Parameter]
    public required List<HealthBucket> Buckets { get; set; }

    [Parameter]
    public bool IsDaily { get; set; }

    private int LabelStep =>
        (IsDaily ? s_daySteps : s_hourSteps).FirstOrDefault(
            step => Buckets.Count / step <= MaximumLabels,
            IsDaily ? s_daySteps[^1] : s_hourSteps[^1]
        );

    private string Label(HealthBucket bucket)
    {
        var localStart = bucket.StartUtc.ToLocalTime();
        if (IsDaily)
        {
            return (localStart.Date - Buckets[0].StartUtc.ToLocalTime().Date).Days % LabelStep == 0
                ? Formatter.FormatLocal(bucket.StartUtc, "dd MMM")
                : string.Empty;
        }

        // Midnight Names The Day When Several Days Show
        return localStart.Hour % LabelStep != 0 ? string.Empty
            : localStart.Hour == 0 && Buckets.Count > 24
                ? Formatter.FormatLocal(bucket.StartUtc, "ddd")
            : Formatter.FormatLocal(bucket.StartUtc, "HH'h'");
    }

    private static string CellClass(HealthState state) =>
        $"health-cell health-{state.ToString().ToLowerInvariant()}";

    private string Describe(HealthBucket bucket)
    {
        var when = IsDaily
            ? Formatter.FormatLocal(bucket.StartUtc, "yyyy-MM-dd dddd")
            : $"{Formatter.FormatWhen(bucket.StartUtc)} To {Formatter.FormatLocal(bucket.EndUtc, "HH:mm")}";
        var what =
            bucket.State == HealthState.NoData ? "Not Watched"
            : bucket.ProblemCount == 0 ? "No Problems"
            : Formatter.FormatCount(bucket.ProblemCount, "Problem");
        var ping = bucket.InternetPing is { } internetPing
            ? $", Internet {Formatter.FormatNumber(Math.Round(internetPing), "ms")}"
            : string.Empty;
        return $"{when}, {what}{ping}";
    }
}
