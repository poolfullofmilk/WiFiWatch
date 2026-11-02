using MudBlazor;
using WiFiWatch.Data.Enums;

namespace WiFiWatch.Desktop.Theming;

public static class EventKindColors
{
    // Harmless Rows Are Coloured By Kind, Problems By Severity
    private static Color ForInfo(EventKind kind) =>
        kind switch
        {
            EventKind.DfsReturn
            or EventKind.Reconnect
            or EventKind.WanBack
            or EventKind.Recovered => Color.Success,
            EventKind.LinkChange
            or EventKind.FaultTrace
            or EventKind.UpdateAvailable
            or EventKind.SpeedTest
            or EventKind.DailySummary
            or EventKind.WeeklySummary => Color.Info,
            _ => Color.Default,
        };

    public static string IconFor(EventSeverity severity) =>
        severity switch
        {
            EventSeverity.Critical => Icons.Material.Rounded.Error,
            EventSeverity.Warning => Icons.Material.Rounded.Warning,
            _ => Icons.Material.Rounded.Info,
        };

    public static Color For(EventKind kind, EventSeverity severity) =>
        severity switch
        {
            EventSeverity.Critical => Color.Error,
            EventSeverity.Warning => Color.Warning,
            _ => ForInfo(kind),
        };
}
