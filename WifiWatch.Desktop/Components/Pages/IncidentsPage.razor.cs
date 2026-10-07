using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Components.Shared.Common;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Desktop.Components.Shared.Tables;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Pages;

public partial class IncidentsPage : IDisposable
{
    private static readonly List<SegmentedButtonOption<bool>> s_scopeOptions =
    [
        new(false, "Problems"),
        new(true, "Everything"),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    // Table State
    private DataTable<WifiEvent>? _incidentTable;
    private string _searchTerm = string.Empty;
    private bool _isEverything;

    protected override void OnInitialized() => Monitor.EventRecorded += OnEventRecorded;

    private void OnEventRecorded() => InvokeAsync(ReloadAsync);

    private Task ReloadAsync() => _incidentTable?.ReloadAsync() ?? Task.CompletedTask;

    private async Task SelectScopeAsync(bool isEverything)
    {
        _isEverything = isEverything;
        await ReloadAsync();
    }

    private async Task<TableData<WifiEvent>> LoadIncidentsAsync(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        await using var context = new WifiDbContext();
        var query = context.Events.AsNoTracking();

        // Problems Hide Notes And Warnings Too Short To Notify
        if (!_isEverything)
        {
            query = query.Where(Problems.IsProblem);
        }

        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            var pattern = $"%{_searchTerm}%";
            query = query.Where(wifiEvent =>
                EF.Functions.Like(wifiEvent.Message, pattern)
                || EF.Functions.Like(wifiEvent.Scope, pattern)
            );
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByColumn(
                wifiEvent => wifiEvent.OccurredAtUtc,
                state.SortDirection != SortDirection.Ascending
            )
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(cancellationToken);

        return new() { Items = items, TotalItems = total };
    }

    private async Task OpenDetailsAsync(TableRowClickEventArgs<WifiEvent> click)
    {
        if (click.Item is { } wifiEvent)
        {
            await IncidentDialog.ShowAsync(DialogService, wifiEvent);
        }
    }

    private static async Task ExportAsync(DateTime? day)
    {
        var (startUtc, endUtc) = CsvExport.DayRangeUtc(day);
        await using var context = new WifiDbContext();
        var events = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc && wifiEvent.OccurredAtUtc < endUtc
            )
            .OrderBy(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();

        CsvExport.Save(
            "Events",
            day,
            [
                [
                    "Start",
                    "End",
                    "Duration",
                    "Kind",
                    "Severity",
                    "Where",
                    "Message",
                    "Windows Reason",
                    "Trace",
                    "Context",
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
                        wifiEvent.Severity,
                        wifiEvent.Scope,
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
