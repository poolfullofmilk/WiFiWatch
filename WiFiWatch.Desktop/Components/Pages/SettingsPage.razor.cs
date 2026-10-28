using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop.Components.Shared.Dialogs;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Desktop.Components.Pages;

public partial class SettingsPage
{
    private const string MaximumPerformance = "Maximum Performance";

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    private bool _isChecking;
    private bool _isInstalling;

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

    private void SaveSettings(UserSettings settings)
    {
        Monitor.Settings = settings;
        settings.Save();
    }

    private string SelectedLanguage =>
        string.IsNullOrWhiteSpace(Monitor.Settings.Language)
            ? string.Empty
            : AppText.Culture.TwoLetterISOLanguageName;

    private void SaveLanguage(string language)
    {
        AppText.Select(language);
        SaveSettings(
            Monitor.Settings with
            {
                Language = string.IsNullOrEmpty(language) ? null : language,
            }
        );
    }

    private void SaveStartWithWindows(bool isStarted)
    {
        StartupRegistration.Apply(isStarted);
        SaveSettings(Monitor.Settings with { StartWithWindows = isStarted });
    }

    private async Task CheckForUpdateAsync()
    {
        _isChecking = true;
        var latest = await Monitor.CheckForUpdateAsync();
        _isChecking = false;
        if (latest is null)
        {
            Snackbar.Add(AppText.Get("GitHubUnavailable"), Severity.Warning);
        }
        else if (latest <= AppInfo.Version)
        {
            Snackbar.Add(
                AppText.Get("VersionLatest", AppInfo.Version.ToString(2)),
                Severity.Success
            );
        }
    }

    private async Task InstallAsync(Version version)
    {
        // The Button Stays Blue, So Ignore A Second Click
        if (_isInstalling)
            return;

        _isInstalling = true;
        await UpdateDialog.InstallAsync(version, Snackbar, Window);
        _isInstalling = false;
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
}
