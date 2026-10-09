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

    [Parameter]
    public EventCallback<string?> OnDebounceIntervalElapsed { get; set; }

    private string? _value;

    protected override void OnParametersSet()
    {
        _value = Value;
    }

    private async Task HandleValueChangedAsync(string? value)
    {
        _value = value;
        await ValueChanged.InvokeAsync(value);
    }

    private Task HandleDebounceIntervalElapsedAsync(string? value) =>
        OnDebounceIntervalElapsed.InvokeAsync(value);
}
