using Microsoft.AspNetCore.Components;

namespace WifiWatch.Components.Shared;

public partial class SegmentedButtonGroup<TValue>
{
    [Parameter]
    public required List<SegmentedButtonOption<TValue>> Options { get; set; }

    [Parameter]
    public TValue? Value { get; set; }

    [Parameter]
    public EventCallback<TValue> ValueChanged { get; set; }

    private bool IsSelected(TValue optionValue) =>
        EqualityComparer<TValue>.Default.Equals(optionValue, Value);

    private async Task OnOptionSelectedAsync(TValue optionValue)
    {
        // Skip When The Choice Is Already Active
        if (IsSelected(optionValue))
            return;

        Value = optionValue;
        await ValueChanged.InvokeAsync(optionValue);
    }
}

public sealed record SegmentedButtonOption<TValue>(TValue Value, string Label, string? Icon = null);
