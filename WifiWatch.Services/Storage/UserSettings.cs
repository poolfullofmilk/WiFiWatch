using System.IO;
using System.Text.Json;
using WifiWatch.Data;

namespace WifiWatch.Services.Storage;

public sealed record UserSettings(
    bool StartWithWindows = true,
    int WeakSignalRssi = -67,
    int SlowLinkMbps = 600,
    int RouterPingMilliseconds = 10,
    int InternetPingMilliseconds = 40,
    int JitterMilliseconds = 20,
    int PacketLossPercent = 2,
    int SlowDnsMilliseconds = 150,
    bool DailySummaryNotification = true,
    bool NightlySpeedTest = false,
    bool ShowUpdatePopup = true
)
{
    private static readonly string s_filePath = Path.Combine(
        WifiDbContext.DataDirectory,
        "settings.json"
    );

    public static UserSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(s_filePath)) ?? new();
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
