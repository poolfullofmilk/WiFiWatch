using System.IO;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Localization;
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

        // A Debug Build Has No Release Exe To Replace
        if (!Updater.CanInstall)
        {
            QuickActions.Open(UpdateChecker.LatestReleaseUrl);
            return;
        }

        _isInstalling = true;
        try
        {
            Updater.Launch(await Updater.DownloadAsync(Version));
            Window.Exit();
        }
        catch (Exception exception)
        {
            Snackbar.Add(
                AppText.Get("Update Failed {0}", exception.GetType().Name),
                Severity.Warning
            );

            // A Bad Checksum Or A Protected Folder Needs The Release Page
            if (exception is InvalidDataException or UnauthorizedAccessException)
                QuickActions.Open(UpdateChecker.LatestReleaseUrl);
        }
        _isInstalling = false;
    }

    private void Close() => Dialog.Close();
}
