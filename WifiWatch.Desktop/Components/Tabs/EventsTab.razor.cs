using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Desktop.Components.Shared.Dialogs;
using WifiWatch.Desktop.Components.Shared.Menus;
using WifiWatch.Desktop.Components.Shared.Tables;
using WifiWatch.Desktop.Theming;
using WifiWatch.Services.Monitoring;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Tabs;

public partial class EventsTab
{
    private static readonly List<FilterMenu.FilterOption> s_kindOptions =
    [
        .. Enum.GetValues<EventKind>()
            .Select(kind => new FilterMenu.FilterOption(
                kind.ToString(),
                kind.ToLabel(),
                EventKindColors.For(kind)
            )),
    ];

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Parameter]
    public EventCallback<int> TotalChanged { get; set; }

    // Table State
    private readonly HashSet<string> _selectedKinds = [.. Enum.GetNames<EventKind>()];
    private DataTable<WifiEvent>? _eventTable;
    private string _searchTerm = string.Empty;

    public Task ReloadAsync() => _eventTable?.ReloadAsync() ?? Task.CompletedTask;

    private async Task<TableData<WifiEvent>> LoadEventsAsync(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        await using var context = new WifiDbContext();
        var selectedKinds = _selectedKinds.Select(Enum.Parse<EventKind>).ToList();
        var query = context
            .Events.AsNoTracking()
            .Where(wifiEvent => selectedKinds.Contains(wifiEvent.Kind));
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
        await TotalChanged.InvokeAsync(total);

        return new() { Items = items, TotalItems = total };
    }

    private async Task OpenDetailsAsync(TableRowClickEventArgs<WifiEvent> click)
    {
        if (click.Item is { } wifiEvent)
        {
            await IncidentDialog.ShowAsync(DialogService, wifiEvent);
        }
    }

    private static string FormatDuration(WifiEvent wifiEvent)
    {
        // Instants End Where They Start, Open Incidents Have No End
        return wifiEvent.EndedAtUtc is not { } endedAtUtc ? "Ongoing"
            : endedAtUtc == wifiEvent.OccurredAtUtc ? "-"
            : Formatter.FormatDuration(endedAtUtc - wifiEvent.OccurredAtUtc);
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
                        FormatDuration(wifiEvent),
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
}
