using System.IO;
using System.Text.Json;
using WiFiWatch.Data;

namespace WiFiWatch.Services.Storage;

public sealed record UserSettings(
    bool StartWithWindows = true,
    int WeakSignalRssi = -70,
    int RouterPingMilliseconds = 20,
    int InternetPingMilliseconds = 60,
    bool DailySummaryNotification = true,
    bool NightlySpeedTest = false,
    bool ReadWiFiNatively = false,
    bool ShowUpdatePopup = true
)
{
    private static readonly string s_filePath = Path.Combine(
        WiFiDbContext.DataDirectory,
        "settings.json"
    );

    // Pre 1.6 Files Spell Their Keys With Wifi
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static UserSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(
                    File.ReadAllText(s_filePath),
                    s_readOptions
                ) ?? new();
        }
        catch
        {
            // A Missing Or Damaged File Means Defaults
            return new();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(s_filePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Losing Settings Must Never Stop Watching
        }
    }
}
