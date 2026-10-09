using System.Reflection;

namespace WiFiWatch.Data.Helpers;

public static class AppInfo
{
    public const string DisplayName = "Wi-Fi Watch";

    // The Exe Carries The Version, Not This Library
    public static Version Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
}
