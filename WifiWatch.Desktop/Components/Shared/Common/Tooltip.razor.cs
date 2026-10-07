using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WifiWatch.Desktop.Components.Shared.Common;

public partial class Tooltip
{
    // Parameters
    [Parameter]
    public string? Text { get; set; }

    [Parameter]
    public required RenderFragment ChildContent { get; set; }

    [Parameter]
    public string? RootClass { get; set; }

    [Parameter]
    public Placement Placement { get; set; } = Placement.Bottom;
}
