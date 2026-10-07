using System.Reflection;

namespace WifiWatch.Data.Helpers;

public static class AppInfo
{
    public const string DisplayName = "Wifi Watch";

    // The Exe Carries The Version, Not This Library
    public static Version Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
}
