using System.Linq.Expressions;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;

namespace WifiWatch.Data.Helpers;

public static class Problems
{
    // A Warning Counts Once It Lasts As Long As A Notification Waits
    public const int WarningMinutes = 5;

    public static readonly Expression<Func<WifiEvent, bool>> IsProblem = wifiEvent =>
        wifiEvent.Severity == EventSeverity.Critical
        || (
            wifiEvent.Severity == EventSeverity.Warning
            && (
                wifiEvent.EndedAtUtc == null
                || wifiEvent.EndedAtUtc >= wifiEvent.OccurredAtUtc.AddMinutes(WarningMinutes)
            )
        );

    public static readonly Func<WifiEvent, bool> IsProblemInMemory = IsProblem.Compile();
}
