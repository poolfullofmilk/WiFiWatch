using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Services.Integration;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Pages;

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
        // The Daily Check May Not Have Run Yet
        if (Monitor.Adapter is null)
        {
            await Monitor.InspectAdapterAsync();
        }
    }

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

    private async Task CheckForUpdateAsync()
    {
        _isChecking = true;
        var latest = await Monitor.CheckForUpdateAsync();
        _isChecking = false;
        if (latest is null)
        {
            Snackbar.Add("Could Not Reach GitHub", Severity.Warning);
        }
        else if (latest <= AppInfo.Version)
        {
            Snackbar.Add($"Version {AppInfo.Version.ToString(2)} Is The Latest", Severity.Success);
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
            ? $"Driver {adapter.DriverVersion} From {driverDate:yyyy-MM-dd}"
            : $"Driver {adapter.DriverVersion ?? "-"}";

    private static Color DriverColor(AdapterInfo adapter) =>
        adapter.DriverDate is null ? Color.Default
        : adapter.IsDriverOld ? Color.Warning
        : Color.Success;

    private static Color PowerColor(string? powerSaving) =>
        powerSaving is null or MaximumPerformance ? Color.Default : Color.Warning;
}
