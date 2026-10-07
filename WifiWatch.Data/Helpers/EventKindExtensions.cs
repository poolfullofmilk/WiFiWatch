using System.Text.RegularExpressions;
using WifiWatch.Data.Enums;

namespace WifiWatch.Data.Helpers;

public static partial class EventKindExtensions
{
    public static string ToLabel(this EventKind kind) =>
        kind switch
        {
            EventKind.WanDown => "Internet Down",
            EventKind.WanBack => "Internet Back",
            _ => WordBoundary()
                .Replace(kind.ToString(), " ")
                .Replace("Dfs", "DFS")
                .Replace("Dns", "DNS"),
        };

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
