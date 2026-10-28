using System.Reflection;

namespace WiFiWatch.Data.Helpers;

public static class AppInfo
{
    public const string DisplayName = "Wi-Fi Watch";

    // The Exe Carries The Version, Not This Library
    public static Version Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public static string VersionText => ShortText(Version);

    // Workflow Builds Carry The Commit They Were Built From, Local Builds None
    public static string? Commit { get; } =
        Assembly
            .GetEntryAssembly()
            ?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Commit")
            ?.Value;

    // Tags, Exe Names And Text Drop Trailing Zero Parts: 2, 2.0.1, 2.1
    public static string ShortText(Version version) =>
        version.ToString(
            version.Build > 0 ? 3
            : version.Minor > 0 ? 2
            : 1
        );

    // Version.TryParse Rejects A One Part Tag Like v2
    public static Version? ParseTag(string tag)
    {
        var number = tag.TrimStart('v', 'V');
        return Version.TryParse(number.Contains('.') ? number : number + ".0", out var version)
            ? version
            : null;
    }
}
