using Microsoft.AspNetCore.Components;

namespace WiFiWatch.Desktop.Components.Shared.Tables;

public partial class SearchTextField
{
    [Parameter]
    public string? Value { get; set; }

    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }

    [Parameter]
    public string PlaceHolder { get; set; } = string.Empty;
}
