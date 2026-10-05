using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WifiWatch.Components.Shared;

public partial class DataTable<TItem>
{
    [Parameter]
    public required Func<
        TableState,
        CancellationToken,
        Task<TableData<TItem>>
    > ServerData { get; set; }

    [Parameter]
    public string SearchTerm { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> SearchTermChanged { get; set; }

    [Parameter]
    public RenderFragment? ToolBarExtra { get; set; }

    [Parameter]
    public RenderFragment? HeaderContent { get; set; }

    [Parameter]
    public required RenderFragment<TItem> RowTemplate { get; set; }

    // Persisted Rows Per Page
    private int _rowsPerPage = 50;

    private MudTable<TItem>? _mudTable;

    private async Task<TableData<TItem>> ServerDataWrapper(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        _rowsPerPage = state.PageSize;
        return await ServerData(state, cancellationToken);
    }

    public Task ReloadAsync() => _mudTable?.ReloadServerData() ?? Task.CompletedTask;

    private async Task OnSearchTermChangedAsync(string? value)
    {
        SearchTerm = value ?? string.Empty;
        await SearchTermChanged.InvokeAsync(SearchTerm);
    }
}
