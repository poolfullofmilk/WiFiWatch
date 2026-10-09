using Microsoft.AspNetCore.Components;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class SettingSwitch
{
    // Parameters
    [Parameter]
    public required string Title { get; set; }

    [Parameter]
    public string? Description { get; set; }

    [Parameter]
    public bool Value { get; set; }

    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }

    [Parameter]
    public bool Disabled { get; set; }
}
