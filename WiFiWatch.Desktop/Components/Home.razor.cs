using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Desktop.Components.Shared.Common;
using WiFiWatch.Desktop.Components.Shared.Dialogs;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components;

public partial class Home : IDisposable
{
    private static PageLink[] Pages =>
        [
            new(Page.Overview, AppText.Get("Overview"), Icons.Material.Rounded.SpaceDashboard),
            new(
                Page.Incidents,
                AppText.Get("Incidents"),
                Icons.Material.Rounded.NotificationsActive
            ),
            new(Page.History, AppText.Get("History"), Icons.Material.Rounded.Insights),
            new(Page.Settings, AppText.Get("Settings"), Icons.Material.Rounded.Settings),
        ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required IJSRuntime JavaScript { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    private int _languageVersion;
    private bool _languageChanged = true;

    private void OnLanguageChanged() =>
        InvokeAsync(() =>
        {
            _languageVersion++;
            _languageChanged = true;
            StateHasChanged();
        });

    // Page State
    private MudMessageBox? _closeMessageBox;
    private PageErrorBoundary? _errorBoundary;
    private Page _page = Page.Overview;
    private Color? _problemColor;
    private bool _hasOfferedUpdate;

    protected override async Task OnInitializedAsync()
    {
        AppText.Changed += OnLanguageChanged;
        Monitor.EventRecorded += OnEventRecorded;
        Monitor.UpdateFound += OnUpdateFound;
        Window.CloseRequested += OnCloseRequested;
        await LoadProblemColorAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_languageChanged)
        {
            _languageChanged = false;
            await JavaScript.InvokeVoidAsync(
                "wifiwatch.setLanguage",
                AppText.Culture.Name,
                AppText.Get("An Unhandled Error Has Occurred"),
                AppText.Get("Reload")
            );
        }
        if (firstRender)
            await OfferUpdateAsync();
    }

    private void OnEventRecorded() => InvokeAsync(LoadProblemColorAsync);

    private void OnUpdateFound() =>
        InvokeAsync(async () =>
        {
            StateHasChanged();
            await OfferUpdateAsync();
        });

    private Task OnCloseRequested() => InvokeAsync(AskBeforeClosingAsync);

    private void ShowPage(Page page)
    {
        _page = page;
        _errorBoundary?.Recover();
    }

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
        // Found By The Daily Check, Offered Once Per Run
        if (_hasOfferedUpdate || Monitor.AvailableUpdate is not { } version)
            return;

        _hasOfferedUpdate = true;
        await UpdateDialog.ShowAsync(DialogService, version);
    }

    private async Task CheckForUpdatesAsync()
    {
        // This Check Shows Its Own Dialog, So The Popup It Triggers Stays Away
        var wasOffered = _hasOfferedUpdate;
        _hasOfferedUpdate = true;
        var latest = await Monitor.CheckForUpdateAsync();
        if (latest is not null && latest > AppInfo.Version)
        {
            StateHasChanged();
            await UpdateDialog.ShowAsync(DialogService, latest);
            return;
        }

        _hasOfferedUpdate = wasOffered;
        Snackbar.Add(
            AppText.Get(latest is null ? "UnableToCheckForUpdates" : "AlreadyLatestVersion"),
            latest is null ? Severity.Warning : Severity.Success
        );
    }

    private Task<IDialogReference> ShowAboutAsync() =>
        DialogService.ShowAsync<AboutDialog>(
            title: null,
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true }
        );

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
        AppText.Changed -= OnLanguageChanged;
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
