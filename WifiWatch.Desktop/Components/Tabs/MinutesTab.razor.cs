using Microsoft.EntityFrameworkCore;
using MudBlazor;
using WifiWatch.Data;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.Models;
using WifiWatch.Desktop.Components.Shared.Tables;
using WifiWatch.Services.Storage;

namespace WifiWatch.Desktop.Components.Tabs;

public partial class MinutesTab
{
    // Table State
    private DataTable<MinuteSample>? _minuteTable;
    private string _searchTerm = string.Empty;

    // Focus From A Chart Click
    private DateTime? _focusStartUtc;
    private DateTime? _focusEndUtc;

    private string FocusLabel =>
        _focusStartUtc is { } startUtc && _focusEndUtc is { } endUtc
            ? $"{Formatter.FormatLocal(startUtc, "yyyy-MM-dd HH:mm")} To {Formatter.FormatLocal(endUtc, "HH:mm")}"
            : string.Empty;

    public async Task FocusAsync(DateTime startUtc, DateTime endUtc)
    {
        _focusStartUtc = startUtc;
        _focusEndUtc = endUtc;
        StateHasChanged();
        await ReloadAsync();
    }

    private async Task ClearFocusAsync()
    {
        _focusStartUtc = null;
        _focusEndUtc = null;
        await ReloadAsync();
    }

    private Task ReloadAsync() => _minuteTable?.ReloadAsync() ?? Task.CompletedTask;

    private async Task<TableData<MinuteSample>> LoadMinutesAsync(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        await using var context = new WifiDbContext();
        var query = context.MinuteSamples.AsNoTracking();
        if (_focusStartUtc is { } focusStartUtc && _focusEndUtc is { } focusEndUtc)
        {
            query = query.Where(sample =>
                sample.MinuteUtc >= focusStartUtc && sample.MinuteUtc < focusEndUtc
            );
        }

        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            var pattern = $"%{_searchTerm}%";
            query = query.Where(sample =>
                EF.Functions.Like(sample.Link, pattern) || EF.Functions.Like(sample.Ssid, pattern)
            );
        }

        var isDescending = state.SortDirection != SortDirection.Ascending;
        var sortedQuery = state.SortLabel switch
        {
            nameof(MinuteSample.Rssi) => query.OrderByColumn(sample => sample.Rssi, isDescending),
            nameof(MinuteSample.RouterPingMilliseconds) => query.OrderByColumn(
                sample => sample.RouterPingMilliseconds,
                isDescending
            ),
            nameof(MinuteSample.InternetPingMilliseconds) => query.OrderByColumn(
                sample => sample.InternetPingMilliseconds,
                isDescending
            ),
            nameof(MinuteSample.DnsMilliseconds) => query.OrderByColumn(
                sample => sample.DnsMilliseconds,
                isDescending
            ),
            _ => query.OrderByColumn(sample => sample.MinuteUtc, isDescending),
        };

        return new()
        {
            TotalItems = await query.CountAsync(cancellationToken),
            Items = await sortedQuery
                .Skip(state.Page * state.PageSize)
                .Take(state.PageSize)
                .ToListAsync(cancellationToken),
        };
    }

    private static string FormatChannel(int? channel) =>
        channel is null ? "-"
        : WifiChannels.IsDfs(channel) ? $"{channel} DFS"
        : $"{channel}";

    private static string FormatRate(MinuteSample sample) =>
        sample.ReceiveRateMbps is null
            ? "-"
            : $"{sample.ReceiveRateMbps}/{sample.TransmitRateMbps} Mbps";

    private static async Task ExportAsync(DateTime? day)
    {
        var (startUtc, endUtc) = CsvExport.DayRangeUtc(day);
        await using var context = new WifiDbContext();
        var samples = await context
            .MinuteSamples.AsNoTracking()
            .Where(sample => sample.MinuteUtc >= startUtc && sample.MinuteUtc < endUtc)
            .OrderBy(sample => sample.MinuteUtc)
            .ToListAsync();

        CsvExport.Save(
            "Minutes",
            day,
            [
                [
                    "Time",
                    "Link",
                    "Network",
                    "Band",
                    "Channel",
                    "DFS",
                    "Signal dBm",
                    "Receive Mbps",
                    "Transmit Mbps",
                    "Router Ping ms",
                    "Router Jitter ms",
                    "Router Loss %",
                    "Internet Ping ms",
                    "Internet Jitter ms",
                    "Internet Loss %",
                    "DNS ms",
                    "Reference DNS ms",
                ],
                .. samples.Select(sample =>
                    new object?[]
                    {
                        Formatter.FormatLocal(sample.MinuteUtc, "yyyy-MM-dd HH:mm"),
                        sample.Link,
                        sample.Ssid,
                        sample.Band,
                        sample.Channel,
                        WifiChannels.IsDfs(sample.Channel) ? "Yes" : "No",
                        sample.Rssi,
                        sample.ReceiveRateMbps,
                        sample.TransmitRateMbps,
                        sample.RouterPingMilliseconds,
                        sample.RouterJitterMilliseconds,
                        sample.RouterLossPercent,
                        sample.InternetPingMilliseconds,
                        sample.InternetJitterMilliseconds,
                        sample.InternetLossPercent,
                        sample.DnsMilliseconds,
                        sample.ReferenceDnsMilliseconds,
                    }
                ),
            ]
        );
    }
}
