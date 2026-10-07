using Microsoft.AspNetCore.Components;

namespace WifiWatch.Desktop.Components.Shared.Common;

public partial class Header
{
    [Parameter]
    public required string Title { get; set; }

    [Parameter]
    public RenderFragment? TitleContent { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
