using System.Linq.Expressions;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Models;

namespace WiFiWatch.Data.Helpers;

public static class Problems
{
    // A Warning Counts Once It Lasts As Long As A Notification Waits
    public const int WarningMinutes = 5;

    public static readonly Expression<Func<WiFiEvent, bool>> IsProblem = wifiEvent =>
        wifiEvent.Severity == EventSeverity.Critical
        || (
            wifiEvent.Severity == EventSeverity.Warning
            && (
                wifiEvent.EndedAtUtc == null
                || wifiEvent.EndedAtUtc >= wifiEvent.OccurredAtUtc.AddMinutes(WarningMinutes)
            )
        );

    public static readonly Func<WiFiEvent, bool> IsProblemInMemory = IsProblem.Compile();
}
