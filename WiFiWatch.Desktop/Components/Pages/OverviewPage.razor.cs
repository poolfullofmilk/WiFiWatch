using System.Windows;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WiFiWatch.Data;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop.Components.Shared.Dialogs;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Pages;

public partial class OverviewPage : IDisposable
{
    private const int RecentCount = 6;
    private const int DayHours = 24;

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required MainWindow Window { get; set; }

    [Parameter]
    public EventCallback IncidentsRequested { get; set; }

    // Page State
    private List<WiFiEvent> _problems = [];
    private List<WiFiEvent> _recent = [];
    private List<HealthBucket> _buckets = [];
    private PeriodSummary? _summary;
    private bool _isLoading = true;

    private string DaySummary
    {
        get
        {
            if (_summary is not { MonitoredMinutes: > 0 } summary)
                return AppText.Get("Nothing Watched Yet");

            // Online Only Means Something Next To How Long Was Watched
            var watched = Formatter.FormatDuration(TimeSpan.FromMinutes(summary.MonitoredMinutes));
            var problems =
                summary.ProblemCount == 0
                    ? AppText.Get("No Problems")
                    : Formatter.FormatCount(summary.ProblemCount, "Problem");
            return AppText.Get(
                "Watched {0}, {1:0.#}% Online, {2}",
                watched,
                summary.OnlinePercent,
                problems
            );
        }
    }

    protected override async Task OnInitializedAsync()
    {
        // Reloads The Moment New Data Lands, Nothing Polls
        Monitor.EventRecorded += OnDataRecorded;
        Monitor.MinuteRecorded += OnDataRecorded;
        Window.IsVisibleChanged += OnVisibleChanged;
        await LoadAsync();
    }

    private void OnDataRecorded() => InvokeAsync(LoadWhileSeenAsync);

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs) =>
        InvokeAsync(LoadWhileSeenAsync);

    private Task LoadWhileSeenAsync()
    {
        // A Hidden Window Catches Up Once Shown
        return Window.IsVisible ? LoadAsync() : Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        // The Last 24 Whole Hours, The Current One Included
        var nowUtc = DateTime.UtcNow;
        var currentHour = DateTime.Today.AddHours(DateTime.Now.Hour);
        var startsUtc = HealthBuckets.Starts(currentHour.AddHours(1 - DayHours), DayHours, false);
        var startUtc = startsUtc[0];

        await using var context = new WiFiDbContext();
        var endUtc = currentHour.AddHours(1).ToUniversalTime();
        var hours = await SummaryWriter.LoadHoursAsync(context, startUtc, endUtc);
        var incidents = await context
            .Events.AsNoTracking()
            .Where(wifiEvent => wifiEvent.EndedAtUtc == null || wifiEvent.EndedAtUtc >= startUtc)
            .Where(Problems.IsProblem)
            .ToListAsync();
        _recent = await context
            .Events.AsNoTracking()
            .Where(Problems.IsProblem)
            .OrderByDescending(wifiEvent => wifiEvent.OccurredAtUtc)
            .Take(RecentCount)
            .ToListAsync();

        _buckets = HealthBuckets.Build(hours, incidents, startsUtc, endUtc, nowUtc);
        _problems = await EventJournal.OpenProblemsAsync();
        _summary = await SummaryWriter.SummarizeAsync(context, hours, startUtc, endUtc);
        _isLoading = false;
        StateHasChanged();
    }

    private Task OpenDetailsAsync(WiFiEvent incident) =>
        IncidentDialog.ShowAsync(DialogService, incident);

    public void Dispose()
    {
        Monitor.EventRecorded -= OnDataRecorded;
        Monitor.MinuteRecorded -= OnDataRecorded;
        Window.IsVisibleChanged -= OnVisibleChanged;
        GC.SuppressFinalize(this);
    }
}
