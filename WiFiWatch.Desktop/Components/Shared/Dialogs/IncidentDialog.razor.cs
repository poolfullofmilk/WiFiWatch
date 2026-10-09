using Microsoft.AspNetCore.Components;
using MudBlazor;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Shared.Dialogs;

public partial class IncidentDialog
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    [CascadingParameter]
    public required IMudDialogInstance Dialog { get; set; }

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Parameter]
    public required WiFiEvent Event { get; set; }

    private EventDetails Details => EventDetails.Parse(Event.Details);

    private string? FixTarget =>
        QuickActions.ForEvent(Event.Kind, Event.Scope, Monitor.Status.RouterAdminUrl);

    private string TimeRange =>
        Event.EndedAtUtc is not { } endedAtUtc
            ? $"{Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)}, Still Going"
        : endedAtUtc == Event.OccurredAtUtc ? Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)
        : $"{Formatter.FormatLocal(Event.OccurredAtUtc, TimeFormat)} To {Formatter.FormatLocal(endedAtUtc, "HH:mm:ss")}, {Formatter.FormatDuration(endedAtUtc - Event.OccurredAtUtc)}";

    private List<(string Label, string Value)> DetailLines
    {
        get
        {
            var details = Details;
            List<(string Label, string Value)> lines = [];
            if (QuickActions.AdviceFor(Event.Kind, Event.Scope) is { } advice)
                lines.Add(("What To Do", advice));

            if (Event.Scope is { } scope)
                lines.Add(("Where", DescribeScope(scope)));

            if (details.Reason is { } reason)
                lines.Add(("Windows Reason", reason));

            if (details.Context is { } context)
                lines.Add(("At That Moment", context));

            if (details.TraceSummary is { } traceSummary)
                lines.Add(("Trace", traceSummary));

            return lines;
        }
    }

    private static string DescribeScope(string scope) =>
        scope switch
        {
            NetworkMonitor.WiFiScope => "Wi-Fi, Between This PC And Your Router",
            NetworkMonitor.HomeNetworkScope =>
                "Home Network, Your Router, Cables Or Powerline Adapters",
            NetworkMonitor.ProviderScope => "Internet Provider, Past Your Router",
            NetworkMonitor.DnsScope => "DNS, Looking Up Names, Not The Line Itself",
            _ => scope,
        };

    public static Task ShowAsync(IDialogService dialogService, WiFiEvent wifiEvent) =>
        dialogService.ShowAsync<IncidentDialog>(
            null,
            new DialogParameters<IncidentDialog> { { dialog => dialog.Event, wifiEvent } },
            new DialogOptions { BackdropClick = true, MaxWidth = MaxWidth.Medium }
        );

    private void Close() => Dialog.Close();
}
