using Microsoft.AspNetCore.Components;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class Header
{
    [Parameter]
    public required string Title { get; set; }

    // Shown In A Tooltip On An Info Icon After The Title
    [Parameter]
    public string? Info { get; set; }

    [Parameter]
    public RenderFragment? TitleContent { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
