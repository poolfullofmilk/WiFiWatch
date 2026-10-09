using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using WiFiWatch.Services.Integration;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class ActionChip
{
    [Parameter]
    public required string Text { get; set; }

    [Parameter]
    public string? Icon { get; set; }

    [Parameter]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    public string? Target { get; set; }

    private string? ActionLabel => Target is null ? null : QuickActions.Describe(Target);

    // Only Chips With A Target Look And Act Clickable
    private EventCallback<MouseEventArgs> ClickCallback =>
        Target is null
            ? default
            : EventCallback.Factory.Create<MouseEventArgs>(this, () => QuickActions.Open(Target));
}
