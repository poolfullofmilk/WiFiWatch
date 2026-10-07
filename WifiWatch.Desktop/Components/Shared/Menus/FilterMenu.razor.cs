using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WifiWatch.Desktop.Components.Shared.Menus;

public partial class FilterMenu
{
    [Parameter]
    public string TooltipText { get; set; } = "Filter";

    [Parameter]
    public required List<FilterOption> Options { get; set; }

    [Parameter]
    public required HashSet<string> SelectedKeys { get; set; }

    [Parameter]
    public EventCallback<string> OnToggle { get; set; }

    private bool AllSelected =>
        Options.Count > 0 && Options.All(option => SelectedKeys.Contains(option.Key));

    private async Task ToggleAllAsync()
    {
        if (AllSelected)
        {
            SelectedKeys.Clear();
        }
        else
        {
            SelectedKeys.UnionWith(Options.Select(option => option.Key));
        }

        await OnToggle.InvokeAsync(string.Empty);
    }

    private async Task ToggleFilterAsync(string key)
    {
        if (!SelectedKeys.Remove(key))
        {
            SelectedKeys.Add(key);
        }

        await OnToggle.InvokeAsync(key);
    }

    public record FilterOption(string Key, string Label, Color Color = Color.Default);
}
