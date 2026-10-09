using System.Diagnostics;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public sealed class ConditionTracker(EventJournal journal)
{
    // Persistence, A Lone Bad Minute Is Wi-Fi Being Wi-Fi
    private const int ConfirmMinutes = 2;
    private const int ClearMinutes = 2;

    // Baseline From The Last Hour Of Good Minutes
    private const int BaselineMinutes = 60;
    private const int BaselineReadyMinutes = 10;
    private const double BaselineSpreads = 3;
    private const double MedianAbsoluteDeviationScale = 1.4826;

    // Severity
    private const double SevereFactor = 3;
    private const double CriticalLossPercent = 10;

    private readonly Dictionary<string, Condition> _conditions = [];

    static ConditionTracker() => Debug.Assert(SelfTestPasses());

    public event Func<string, Task>? IncidentOpened;

    public void Reset() => _conditions.Clear();

    public async Task EvaluateAsync(Check check)
    {
        if (!_conditions.TryGetValue(check.Key, out var condition))
        {
            condition = new();
            _conditions[check.Key] = condition;
        }

        // Unmeasurable Minutes Clear Too, Or Weak Signal Outlives Wi-Fi
        if (check.Value is not { } value)
        {
            await CountClearMinuteAsync(check, condition);
            return;
        }

        var limit = check.IsHigherWorse
            ? Math.Max(check.Threshold, BaselineLimit(condition.Baseline) ?? check.Threshold)
            : check.Threshold;
        var isBad = check.IsHigherWorse ? value > limit : value < limit;
        var isSevere =
            check.IsHigherWorse
            && (
                check.IsLoss
                    ? value >= CriticalLossPercent
                    : value >= check.Threshold * SevereFactor
            );

        if (isBad)
        {
            await TrackBadMinuteAsync(check, condition, value, isSevere);
            return;
        }

        condition.Baseline.Enqueue(value);
        if (condition.Baseline.Count > BaselineMinutes)
        {
            condition.Baseline.Dequeue();
        }

        await CountClearMinuteAsync(check, condition);
    }

    private async Task CountClearMinuteAsync(Check check, Condition condition)
    {
        condition.BadValues.Clear();
        condition.GoodStreak++;
        if (journal.IsOpen(check.Key) && condition.GoodStreak >= ClearMinutes)
        {
            var endedAtUtc = check.MinuteUtc.AddMinutes(1 - ClearMinutes);
            await journal.CloseAsync(
                check.Key,
                Summarize(check, condition, endedAtUtc - condition.StartedAtUtc),
                endedAtUtc
            );
            condition.IncidentValues.Clear();
        }
    }

    private async Task TrackBadMinuteAsync(
        Check check,
        Condition condition,
        double value,
        bool isSevere
    )
    {
        if (condition.BadValues.Count == 0)
        {
            condition.FirstBadUtc = check.MinuteUtc;
        }

        condition.GoodStreak = 0;
        condition.BadValues.Add(value);
        var severity = isSevere ? EventSeverity.Critical : EventSeverity.Warning;

        if (journal.IsOpen(check.Key))
        {
            condition.IncidentValues.Add(value);
            if (isSevere)
            {
                await journal.UpdateAsync(
                    check.Key,
                    $"{check.Title} {Format(value, check.Unit)}",
                    EventSeverity.Critical
                );
            }

            return;
        }

        if (condition.BadValues.Count < ConfirmMinutes && !isSevere)
            return;

        condition.StartedAtUtc = condition.FirstBadUtc;
        condition.IncidentValues.Clear();
        condition.IncidentValues.AddRange(condition.BadValues);
        await journal.OpenAsync(
            check.Key,
            check.Kind,
            severity,
            $"{check.Title} {Format(value, check.Unit)}",
            check.Scope,
            new EventDetails(Context: check.Context),
            condition.StartedAtUtc
        );

        if (IncidentOpened is not null)
        {
            await IncidentOpened(check.Key);
        }
    }

    private static string Summarize(Check check, Condition condition, TimeSpan duration)
    {
        var values = condition.IncidentValues.Count > 0 ? condition.IncidentValues : [0];
        var worst = check.IsHigherWorse ? values.Max() : values.Min();
        var worstLabel = check.IsHigherWorse ? "Peak" : "Lowest";

        return $"{check.Title} For {Formatter.FormatDuration(duration)}, {worstLabel} {Format(worst, check.Unit)}, Average {Format(values.Average(), check.Unit)}";
    }

    private static double? BaselineLimit(Queue<double> baseline)
    {
        if (baseline.Count < BaselineReadyMinutes)
            return null;

        // Median Plus A Few Robust Spreads, Outliers Barely Move It
        var median = Median(baseline);
        var deviation = Median(baseline.Select(value => Math.Abs(value - median)));
        return median + (BaselineSpreads * MedianAbsoluteDeviationScale * deviation);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static string Format(double value, string unit) =>
        unit == "%" ? $"{value:0.#}%" : $"{value:0.#} {unit}";

    private static bool SelfTestPasses()
    {
        var steady = new Queue<double>([2, 3, 2, 4, 3, 2, 3, 5, 2, 3, 4, 3]);
        return Median([3, 1, 2]) == 2
            && Median([1, 2, 3, 4]) == 2.5
            && BaselineLimit(new Queue<double>([1, 2])) is null
            && BaselineLimit(steady) is > 7 and < 8
            && Format(3.25, "%") == $"{3.25:0.#}%";
    }

    public sealed record Check(
        string Key,
        EventKind Kind,
        string Title,
        string Unit,
        double? Value,
        double Threshold,
        bool IsHigherWorse,
        bool IsLoss,
        string Scope,
        string Context,
        DateTime MinuteUtc
    );

    private sealed class Condition
    {
        public Queue<double> Baseline { get; } = new();

        public List<double> BadValues { get; } = [];

        public List<double> IncidentValues { get; } = [];

        public DateTime FirstBadUtc { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public int GoodStreak { get; set; }
    }
}
