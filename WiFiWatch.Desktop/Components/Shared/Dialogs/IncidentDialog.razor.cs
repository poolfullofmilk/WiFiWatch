using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WiFiWatch.Data;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Shared.Dialogs;

public partial class IncidentDialog : IDisposable
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    [CascadingParameter]
    public required IMudDialogInstance Dialog { get; set; }

    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    [Parameter]
    public required WiFiEvent Event { get; set; }

    // An Open Incident Ends Or Gains Its Trace Later
    private WiFiEvent? _latest;

    private WiFiEvent Current => _latest ?? Event;

    private EventDetails Details => EventDetails.Parse(Current.Details);

    private string? FixTarget =>
        QuickActions.ForEvent(Current.Kind, Current.Scope, Monitor.Status.RouterAdminUrl);

    private string TimeRange =>
        Current.EndedAtUtc is not { } endedAtUtc
            ? $"{Formatter.FormatLocal(Current.OccurredAtUtc, TimeFormat)}, Still Going"
        : endedAtUtc == Current.OccurredAtUtc
            ? Formatter.FormatLocal(Current.OccurredAtUtc, TimeFormat)
        : $"{Formatter.FormatLocal(Current.OccurredAtUtc, TimeFormat)} To {Formatter.FormatLocal(endedAtUtc, "HH:mm:ss")}, {Formatter.FormatDuration(endedAtUtc - Current.OccurredAtUtc)}";

    private List<(string Label, string Value)> DetailLines
    {
        get
        {
            var details = Details;
            List<(string Label, string Value)> lines = [];
            if (QuickActions.AdviceFor(Current.Kind, Current.Scope) is { } advice)
                lines.Add(("What To Do", advice));

            if (Current.Scope is { } scope)
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

    protected override void OnInitialized() => Monitor.EventRecorded += OnEventRecorded;

    private void OnEventRecorded() => InvokeAsync(ReloadAsync);

    private async Task ReloadAsync()
    {
        await using var context = new WiFiDbContext();
        _latest =
            await context
                .Events.AsNoTracking()
                .FirstOrDefaultAsync(wifiEvent => wifiEvent.Id == Event.Id)
            ?? _latest;
        StateHasChanged();
    }

    public static Task ShowAsync(IDialogService dialogService, WiFiEvent wifiEvent) =>
        dialogService.ShowAsync<IncidentDialog>(
            null,
            new DialogParameters<IncidentDialog> { { dialog => dialog.Event, wifiEvent } },
            new DialogOptions { BackdropClick = true, MaxWidth = MaxWidth.Medium }
        );

    private void Close() => Dialog.Close();

    public void Dispose()
    {
        Monitor.EventRecorded -= OnEventRecorded;
        GC.SuppressFinalize(this);
    }
}
