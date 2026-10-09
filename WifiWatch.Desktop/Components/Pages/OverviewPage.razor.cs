using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components.Pages;

public partial class OverviewPage : IDisposable
{
    private const int RecentCount = 6;
    private const int DayHours = 24;

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Parameter]
    public EventCallback IncidentsRequested { get; set; }

    // Page State
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromMinutes(1));
    private List<WifiEvent> _problems = [];
    private List<WifiEvent> _recent = [];
    private List<HealthBucket> _buckets = [];
    private PeriodSummary? _summary;
    private bool _isLoading = true;

    private string DaySummary
    {
        get
        {
            if (_summary is not { MonitoredMinutes: > 0 } summary)
                return "Nothing Watched Yet";

            // Online Only Means Something Next To How Long Was Watched
            var watched = Formatter.FormatDuration(TimeSpan.FromMinutes(summary.MonitoredMinutes));
            var problems =
                summary.ProblemCount == 0
                    ? "No Problems"
                    : Formatter.FormatCount(summary.ProblemCount, "Problem");
            return $"Watched {watched}, {summary.OnlinePercent:0.#}% Online, {problems}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Monitor.EventRecorded += OnEventRecorded;
        await LoadAsync();
        _ = RefreshEveryMinuteAsync();
    }

    private void OnEventRecorded() => InvokeAsync(LoadAsync);

    private async Task RefreshEveryMinuteAsync()
    {
        // The Day Strip Rolls On Without Any Event
        while (await _refreshTimer.WaitForNextTickAsync())
        {
            await InvokeAsync(LoadAsync);
        }
    }

    private async Task LoadAsync()
    {
        // The Last 24 Whole Hours, The Current One Included
        var nowUtc = DateTime.UtcNow;
        var currentHour = DateTime.Today.AddHours(DateTime.Now.Hour);
        var startsUtc = HealthBuckets.Starts(currentHour.AddHours(1 - DayHours), DayHours, false);
        var startUtc = startsUtc[0];

        await using var context = new WifiDbContext();
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

    private Task OpenDetailsAsync(WifiEvent incident) =>
        IncidentDialog.ShowAsync(DialogService, incident);

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        _refreshTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
