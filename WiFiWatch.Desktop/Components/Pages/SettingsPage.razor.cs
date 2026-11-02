using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Desktop.Components.Pages;

public partial class SettingsPage
{
    private const string MaximumPerformance = "Maximum Performance";

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Fresh Each Visit, Driver Or Power Plan May Change
        try
        {
            await Monitor.InspectAdapterAsync();
        }
        catch
        {
            // A Failed Look Keeps The Last Known Adapter
        }
    }

    // The Three Limits Share One Card, Each With Its Own Range
    private LimitView[] Limits =>
        [
            new(
                "WeakSignal",
                "SignalDescription",
                Monitor.Settings.WeakSignalRssi,
                -100,
                -30,
                value => Monitor.Settings with { WeakSignalRssi = value }
            ),
            new(
                "RouterPing",
                "RouterDescription",
                Monitor.Settings.RouterPingMilliseconds,
                1,
                1000,
                value => Monitor.Settings with { RouterPingMilliseconds = value }
            ),
            new(
                "InternetPing",
                "InternetDescription",
                Monitor.Settings.InternetPingMilliseconds,
                1,
                2000,
                value => Monitor.Settings with { InternetPingMilliseconds = value }
            ),
        ];

    private void SaveSettings(UserSettings settings)
    {
        Monitor.Settings = settings;
        settings.Save();
    }

    private void SaveStartWithWindows(bool isStarted)
    {
        StartupRegistration.Apply(isStarted);
        SaveSettings(Monitor.Settings with { StartWithWindows = isStarted });
    }

    private static string DriverText(AdapterInfo adapter) =>
        adapter.DriverDate is { } driverDate
            ? AppText.Get("DriverFrom", adapter.DriverVersion ?? "-", driverDate)
            : AppText.Get("Driver", adapter.DriverVersion ?? "-");

    private static Color DriverColor(AdapterInfo adapter) =>
        adapter.DriverDate is null ? Color.Default
        : adapter.IsDriverOld ? Color.Warning
        : Color.Success;

    private static Color PowerColor(string? powerSaving) =>
        powerSaving is null or MaximumPerformance ? Color.Default : Color.Warning;

    private sealed record LimitView(
        string TitleKey,
        string InfoKey,
        int Value,
        int Min,
        int Max,
        Func<int, UserSettings> With
    );
}
