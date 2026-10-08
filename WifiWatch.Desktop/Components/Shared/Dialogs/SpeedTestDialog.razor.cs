using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components.Shared.Dialogs;

public sealed partial class SpeedTestDialog : IDisposable
{
    // Share Of The Bar Each Phase Fills
    private const double PingShare = 0.1;
    private const double DownloadShare = 0.45;

    [CascadingParameter]
    public required IMudDialogInstance Dialog { get; set; }

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    // Test State
    private CancellationTokenSource? _cancellation;
    private SpeedTestProgress? _progress;
    private SpeedTest? _result;
    private string? _error;

    private bool IsRunning => _cancellation is not null;

    private string HeadlineLabel =>
        _result is not null ? "Download"
        : _progress?.Phase is SpeedTestPhase.Download ? "Download"
        : _progress?.Phase is SpeedTestPhase.Upload ? "Upload"
        : IsRunning ? "Measuring Ping"
        : "Stopped";

    private string HeadlineValue =>
        _result is { } result ? $"{result.DownloadMbps:0}"
        : _progress?.CurrentMbps is { } currentMbps ? $"{currentMbps:0}"
        : "-";

    private double OverallPercent
    {
        get
        {
            if (_result is not null)
                return 100;

            // Ping Is Short, Download And Upload Share The Rest
            var fraction = _progress?.PhaseFraction ?? 0;
            return 100
                * (
                    _progress?.Phase switch
                    {
                        SpeedTestPhase.Download => PingShare + (fraction * DownloadShare),
                        SpeedTestPhase.Upload => PingShare
                            + DownloadShare
                            + (fraction * (1 - PingShare - DownloadShare)),
                        _ => fraction * PingShare,
                    }
                );
        }
    }

    private List<(string Label, string Value)> Results =>
        [
            ("Ping", FormatPing(_result?.IdlePingMilliseconds ?? _progress?.IdlePing)),
            ("Download", FormatMbps(_result?.DownloadMbps ?? _progress?.DownloadMbps)),
            ("Upload", FormatMbps(_result?.UploadMbps)),
        ];

    protected override Task OnAfterRenderAsync(bool firstRender) =>
        firstRender ? RunAsync() : Task.CompletedTask;

    private async Task RunAsync()
    {
        _cancellation = new CancellationTokenSource();
        _progress = null;
        _result = null;
        _error = null;
        StateHasChanged();

        // Progress Arrives On The Renderer, So Each Report Redraws
        var progress = new Progress<SpeedTestProgress>(report =>
        {
            _progress = report;
            StateHasChanged();
        });
        _result = await Monitor.RunSpeedTestAsync(progress, _cancellation.Token);
        if (_result is null && !_cancellation.IsCancellationRequested)
        {
            _error = Monitor.Status.IsSpeedTesting
                ? "Another Speed Test Is Already Running"
                : "Speed Test Failed, Check The Connection";
        }

        _cancellation.Dispose();
        _cancellation = null;
        StateHasChanged();
    }

    private static string FormatPing(double? milliseconds) =>
        milliseconds is { } value ? $"{value:0} ms" : "-";

    private static string FormatMbps(double? megabits) =>
        megabits is { } value ? $"{value:0} Mbps" : "-";

    public static Task ShowAsync(IDialogService dialogService) =>
        dialogService.ShowAsync<SpeedTestDialog>(
            title: null,
            options: new DialogOptions
            {
                BackdropClick = false,
                CloseButton = false,
                MaxWidth = MaxWidth.Small,
            }
        );

    private void Close()
    {
        _cancellation?.Cancel();
        Dialog.Close();
    }

    public void Dispose() => _cancellation?.Cancel();
}
