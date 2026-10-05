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

    // Last Good Page, Shown Again When A Load Is Cancelled
    private TableData<TItem> _lastPage = new() { Items = [], TotalItems = 0 };

    private MudTable<TItem>? _mudTable;

    private async Task<TableData<TItem>> ServerDataWrapper(
        TableState state,
        CancellationToken cancellationToken
    )
    {
        _rowsPerPage = state.PageSize;

        try
        {
            _lastPage = await ServerData(state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A Newer Load Replaced This One, MudTable Never Catches It
        }

        return _lastPage;
    }

    public Task ReloadAsync() => _mudTable?.ReloadServerData() ?? Task.CompletedTask;

    private async Task OnSearchTermChangedAsync(string? value)
    {
        SearchTerm = value ?? string.Empty;
        await SearchTermChanged.InvokeAsync(SearchTerm);
    }
}
