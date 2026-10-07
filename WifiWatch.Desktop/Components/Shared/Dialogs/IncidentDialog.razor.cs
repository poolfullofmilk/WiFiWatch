using Microsoft.AspNetCore.Components;
using MudBlazor;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Data.ViewModels;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Desktop.Components.Shared.Dialogs;

public partial class IncidentDialog
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    [CascadingParameter]
    public required IMudDialogInstance Dialog { get; set; }

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Parameter]
    public required WifiEvent Event { get; set; }

    private EventDetails Details => EventDetails.Parse(Event.Details);

    private string TimeRange =>
        Event.EndedAtUtc is not { } endedAtUtc
            ? $"{Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)}, Still Going"
        : endedAtUtc == Event.OccurredAtUtc ? Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)
        : $"{Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)} To {Formatter.FormatLocal(endedAtUtc, "HH:mm:ss")}, {Formatter.FormatDuration(endedAtUtc - Event.OccurredAtUtc)}";

    private IEnumerable<(string Label, string Value)> DetailLines
    {
        get
        {
            var details = Details;
            if (details.Reason is { } reason)
                yield return ("Windows Reason", reason);

            if (details.Context is { } context)
                yield return ("At That Moment", context);

            if (details.TraceSummary is { } traceSummary)
                yield return ("Trace", traceSummary);
        }
    }

    public static Task ShowAsync(IDialogService dialogService, WifiEvent wifiEvent) =>
        dialogService.ShowAsync<IncidentDialog>(
            null,
            new DialogParameters<IncidentDialog> { { dialog => dialog.Event, wifiEvent } },
            new DialogOptions { BackdropClick = true, MaxWidth = MaxWidth.Medium }
        );

    private void Close() => Dialog.Close();
}
