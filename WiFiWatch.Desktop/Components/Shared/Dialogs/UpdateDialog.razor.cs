using System.ComponentModel;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Helpers;
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

    // Bytes So Far, Zero Total While No Length Is Sent
    private long _received;
    private long _total;
    private int _percent;

    private string ProgressText =>
        _total > 0 && _received >= _total ? AppText.Get("StartingUpdate")
        : _total > 0
            ? AppText.Get(
                "DownloadedOf",
                Formatter.FormatSize(_received),
                Formatter.FormatSize(_total)
            )
        : Formatter.FormatSize(_received);

    public static Task ShowAsync(IDialogService dialogService, Version version) =>
        dialogService.ShowAsync<UpdateDialog>(
            null,
            new DialogParameters<UpdateDialog> { { dialog => dialog.Version, version } },
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true }
        );

    private async Task InstallAsync()
    {
        // The Button Stays Blue, So Ignore A Second Click
        if (_isInstalling)
            return;

        // A Debug Build Has No Release Exe To Replace
        if (!Updater.CanInstall)
        {
            OpenReleasePage();
            return;
        }

        // A Running Update Cannot Be Closed Halfway
        _isInstalling = true;
        Window.IsUpdating = true;
        await SetClosableAsync(false);
        try
        {
            var path = await Updater.DownloadAsync(
                Version,
                new Progress<(long, long)>(ShowProgress)
            );
            Updater.Launch(path);
            Window.IsUpdating = false;
            Window.Exit();
        }
        catch (InvalidDataException)
        {
            // An Unverified Download Never Runs, The Release Page Opens Instead
            Snackbar.Add(AppText.Get("UpdateNotVerified"), Severity.Warning);
            OpenReleasePage();
        }
        catch (UnauthorizedAccessException)
        {
            // A Protected Folder Like Program Files Takes A Manual Download
            Snackbar.Add(AppText.Get("UpdateFolderProtected"), Severity.Warning);
            OpenReleasePage();
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or TaskCanceledException
                        or Win32Exception
            )
        {
            Snackbar.Add(AppText.Get("UpdateFailed"), Severity.Error);
            _isInstalling = false;
            await SetClosableAsync(true);
        }
        finally
        {
            Window.IsUpdating = false;
        }
    }

    private void ShowProgress((long Received, long Total) progress)
    {
        // Redraws Once Per Percent, Not Per Chunk
        var (received, total) = progress;
        var percent = total > 0 ? (int)(received * 100 / total) : 0;
        var moved =
            total != _total || (total > 0 ? percent != _percent : received - _received >= 1048576);
        if (!moved)
            return;

        _received = received;
        _total = total;
        _percent = percent;
        _ = InvokeAsync(StateHasChanged);
    }

    private Task SetClosableAsync(bool isClosable) =>
        Dialog.SetOptionsAsync(
            Dialog.Options with
            {
                CloseButton = isClosable,
                CloseOnEscapeKey = isClosable,
                BackdropClick = isClosable,
            }
        );

    private void OpenReleasePage()
    {
        QuickActions.Open(UpdateChecker.LatestReleaseUrl);
        Dialog.Close();
    }
}
