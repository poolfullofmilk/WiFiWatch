using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Shared.Dialogs;

public partial class UpdateDialog
{
    [CascadingParameter]
    public required IMudDialogInstance Dialog { get; set; }

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    [Parameter]
    public required Version Version { get; set; }

    private bool _isInstalling;

    public static Task ShowAsync(IDialogService dialogService, Version version) =>
        dialogService.ShowAsync<UpdateDialog>(
            null,
            new DialogParameters<UpdateDialog> { { dialog => dialog.Version, version } },
            new DialogOptions { BackdropClick = true, MaxWidth = MaxWidth.ExtraSmall }
        );

    public static async Task InstallAsync(Version version, ISnackbar snackbar, MainWindow window)
    {
        // A Debug Build Has No Release Exe To Replace
        if (!Updater.CanInstall)
        {
            QuickActions.Open(UpdateChecker.LatestReleaseUrl);
            return;
        }

        try
        {
            Updater.Launch(await Updater.DownloadAsync(version));
            window.Exit();
        }
        catch (Exception exception)
        {
            snackbar.Add($"Update Failed {exception.GetType().Name}", Severity.Warning);
        }
    }

    private void SetHidden(bool isHidden)
    {
        Monitor.Settings = Monitor.Settings with { ShowUpdatePopup = !isHidden };
        Monitor.Settings.Save();
    }

    private async Task InstallAsync()
    {
        // The Button Stays Blue, So Ignore A Second Click
        if (_isInstalling)
            return;

        _isInstalling = true;
        await InstallAsync(Version, Snackbar, Window);
        _isInstalling = false;
    }

    private void Close() => Dialog.Close();
}
