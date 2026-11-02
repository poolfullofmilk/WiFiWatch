using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WiFiWatch.Data;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop.Components.Shared.Common;
using WiFiWatch.Desktop.Components.Shared.Dialogs;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Desktop.Components.Pages;

public partial class IncidentsPage : IDisposable
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    // Rows Share SoTF.Editor's Height, Which Virtualizing Needs Fixed
    private const int RowHeight = 61;
    private static readonly string s_rowStyle = $"height:{RowHeight}px";

    // List State
    private List<WiFiEvent>? _incidents;
    private string _searchTerm = string.Empty;
    private bool _isEverything;

    protected override async Task OnInitializedAsync()
    {
        Monitor.EventRecorded += OnEventRecorded;
        await LoadIncidentsAsync();
    }

    private void OnEventRecorded() => InvokeAsync(LoadIncidentsAsync);

    private async Task SearchAsync(string? searchTerm)
    {
        // The Field Debounces, So Every Change Reloads, Clearing Too
        _searchTerm = searchTerm ?? string.Empty;
        await LoadIncidentsAsync();
    }

    private async Task SelectScopeAsync(bool isEverything)
    {
        _isEverything = isEverything;
        await LoadIncidentsAsync();
    }

    private async Task LoadIncidentsAsync()
    {
        await using var context = new WiFiDbContext();
        var query = context.Events.AsNoTracking();

        // Problems Hide Notes And Warnings Too Short To Notify
        if (!_isEverything)
        {
            query = query.Where(Problems.IsProblem);
        }

        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            var pattern = $"%{_searchTerm}%";
            var scopes = new[]
            {
                NetworkMonitor.WiFiScope,
                NetworkMonitor.HomeNetworkScope,
                NetworkMonitor.ProviderScope,
                NetworkMonitor.DnsScope,
            }
                .Where(scope =>
                    AppText.Culture.CompareInfo.IndexOf(
                        AppText.Get(scope),
                        _searchTerm,
                        CompareOptions.IgnoreCase
                    ) >= 0
                )
                .ToArray();
            var kinds = Enum.GetValues<EventKind>()
                .Where(kind =>
                    AppText.Culture.CompareInfo.IndexOf(
                        kind.ToLabel(),
                        _searchTerm,
                        CompareOptions.IgnoreCase
                    ) >= 0
                )
                .ToArray();
            query = query.Where(wifiEvent =>
                EF.Functions.Like(wifiEvent.Message, pattern)
                || EF.Functions.Like(wifiEvent.Scope, pattern)
                || scopes.Contains(wifiEvent.Scope!)
                || kinds.Contains(wifiEvent.Kind)
            );
        }

        // ponytail: every match in memory, a year is a few thousand rows; page in SQL if that ever drags
        _incidents = await query
            .OrderByDescending(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();
        StateHasChanged();
    }

    private async Task OpenDetailsAsync(TableRowClickEventArgs<WiFiEvent> click)
    {
        if (click.Item is { } wifiEvent)
        {
            await IncidentDialog.ShowAsync(DialogService, wifiEvent);
        }
    }

    private static async Task ExportAsync(DateTime? day)
    {
        var (startUtc, endUtc) = CsvExport.DayRangeUtc(day);
        await using var context = new WiFiDbContext();
        var events = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc && wifiEvent.OccurredAtUtc < endUtc
            )
            .OrderBy(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();

        CsvExport.Save(
            AppText.Get("Events"),
            day,
            [
                [
                    AppText.Get("Start"),
                    AppText.Get("End"),
                    AppText.Get("Duration"),
                    AppText.Get("Kind"),
                    AppText.Get("Severity"),
                    AppText.Get("Where"),
                    AppText.Get("Message"),
                    AppText.Get("Windows Reason"),
                    AppText.Get("Trace"),
                    AppText.Get("Context"),
                ],
                .. events.Select(wifiEvent =>
                {
                    var details = EventDetails.Parse(wifiEvent.Details);
                    return new object?[]
                    {
                        Formatter.FormatLocal(wifiEvent.OccurredAtUtc, "yyyy-MM-dd HH:mm:ss"),
                        wifiEvent.EndedAtUtc is { } endedAtUtc
                            ? Formatter.FormatLocal(endedAtUtc, "yyyy-MM-dd HH:mm:ss")
                            : null,
                        Formatter.FormatIncidentLength(
                            wifiEvent.OccurredAtUtc,
                            wifiEvent.EndedAtUtc
                        ),
                        wifiEvent.Kind.ToLabel(),
                        AppText.Get(wifiEvent.Severity.ToString()),
                        wifiEvent.Scope is { } scope ? AppText.Get(scope) : null,
                        wifiEvent.Message,
                        details.Reason,
                        details.TraceSummary,
                        details.Context,
                    };
                }),
            ]
        );
    }

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        GC.SuppressFinalize(this);
    }
}
