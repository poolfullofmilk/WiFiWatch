using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Desktop.Components.Tabs;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components;

public partial class Home : IDisposable
{
    private const int MinutesPanelIndex = 1;

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    // Tab State
    private EventsTab? _eventsTab;
    private MinutesTab? _minutesTab;
    private StatsTab? _statsTab;
    private SettingsTab? _settingsTab;
    private MudMessageBox? _closeMessageBox;
    private int _activePanelIndex;
    private int _eventTotal;
    private bool _hasOfferedUpdate;

    protected override void OnInitialized()
    {
        Monitor.EventRecorded += OnEventRecorded;
        Monitor.UpdateFound += OnUpdateFound;
        Window.CloseRequested += OnCloseRequested;
    }

    protected override Task OnAfterRenderAsync(bool firstRender) =>
        firstRender ? OfferUpdateAsync() : Task.CompletedTask;

    private void OnEventRecorded() =>
        InvokeAsync(() => _eventsTab?.ReloadAsync() ?? Task.CompletedTask);

    private void OnUpdateFound() => InvokeAsync(OfferUpdateAsync);

    private Task OnCloseRequested() => InvokeAsync(AskBeforeClosingAsync);

    private async Task OfferUpdateAsync()
    {
        // One Popup Per Run, Unless Turned Off
        if (
            _hasOfferedUpdate
            || !Monitor.Settings.ShowUpdatePopup
            || Monitor.AvailableUpdate is not { } version
        )
            return;

        _hasOfferedUpdate = true;
        await UpdateDialog.ShowAsync(DialogService, version);
    }

    private async Task AskBeforeClosingAsync()
    {
        // Dismissing The Dialog Keeps Everything As It Was
        var keepRunning = await _closeMessageBox!.ShowAsync(
            new DialogOptions { CloseButton = false, BackdropClick = true }
        );

        if (keepRunning == true)
        {
            Window.Hide();
        }
        else if (keepRunning == false)
        {
            Window.Exit();
        }
    }

    private void SetEventTotal(int total)
    {
        _eventTotal = total;
        StateHasChanged();
    }

    private Task OpenStatsAsync() => _statsTab?.OpenAsync() ?? Task.CompletedTask;

    private async Task ShowMinutesAsync((DateTime StartUtc, DateTime EndUtc) range)
    {
        _activePanelIndex = MinutesPanelIndex;
        StateHasChanged();
        if (_minutesTab is not null)
        {
            await _minutesTab.FocusAsync(range.StartUtc, range.EndUtc);
        }
    }

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        Monitor.UpdateFound -= OnUpdateFound;
        Window.CloseRequested -= OnCloseRequested;
        GC.SuppressFinalize(this);
    }
}
