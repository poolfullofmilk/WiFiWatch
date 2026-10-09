using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Data.Enums;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components;

public partial class Home : IDisposable
{
    private static readonly PageLink[] s_pages =
    [
        new(Page.Overview, "Overview", Icons.Material.Rounded.SpaceDashboard),
        new(Page.Incidents, "Incidents", Icons.Material.Rounded.NotificationsActive),
        new(Page.History, "History", Icons.Material.Rounded.Insights),
        new(Page.Settings, "Settings", Icons.Material.Rounded.Settings),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    // Page State
    private MudMessageBox? _closeMessageBox;
    private Page _page = Page.Overview;
    private Color? _problemColor;
    private bool _hasOfferedUpdate;

    protected override async Task OnInitializedAsync()
    {
        Monitor.EventRecorded += OnEventRecorded;
        Monitor.UpdateFound += OnUpdateFound;
        Window.CloseRequested += OnCloseRequested;
        await LoadProblemColorAsync();
    }

    protected override Task OnAfterRenderAsync(bool firstRender) =>
        firstRender ? OfferUpdateAsync() : Task.CompletedTask;

    private void OnEventRecorded() => InvokeAsync(LoadProblemColorAsync);

    private void OnUpdateFound() => InvokeAsync(OfferUpdateAsync);

    private Task OnCloseRequested() => InvokeAsync(AskBeforeClosingAsync);

    private void ShowPage(Page page) => _page = page;

    private async Task LoadProblemColorAsync()
    {
        // A Dot On Overview While Something Is Still Wrong
        var problems = await EventJournal.OpenProblemsAsync();
        _problemColor = problems.FirstOrDefault()?.Severity switch
        {
            EventSeverity.Critical => Color.Error,
            EventSeverity.Warning => Color.Warning,
            _ => null,
        };
        StateHasChanged();
    }

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
            new DialogOptions { BackdropClick = true }
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

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        Monitor.UpdateFound -= OnUpdateFound;
        Window.CloseRequested -= OnCloseRequested;
        GC.SuppressFinalize(this);
    }

    private enum Page
    {
        Overview,
        Incidents,
        History,
        Settings,
    }

    private sealed record PageLink(Page Page, string Label, string Icon);
}
