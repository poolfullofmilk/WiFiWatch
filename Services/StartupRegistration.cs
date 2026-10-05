using System.IO;
using Microsoft.Win32;

namespace WifiWatch.Services;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WifiWatch";

    // Desktop Apps Folder Links To This Start Menu Folder
    private static readonly string s_shortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "01 Apps",
        "WifiWatch.lnk"
    );

    public static void Apply(bool isEnabled)
    {
        // Debug Builds Never Register With Windows
#if !DEBUG
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            // Rewritten Every Launch, The Exe Name Carries The Version
            if (isEnabled)
            {
                runKey.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            WriteShortcut();
        }
        catch
        {
            // A Locked Registry Must Not Stop Watching
        }
#endif
    }

    private static void WriteShortcut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(s_shortcutPath)!);

        // WScript Shell Writes Shortcuts Without A Package
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = shell.CreateShortcut(s_shortcutPath);
        shortcut.TargetPath = Environment.ProcessPath;
        shortcut.Description = "WifiWatch";
        shortcut.Save();
    }
}
