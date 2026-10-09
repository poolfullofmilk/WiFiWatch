using Microsoft.AspNetCore.Components;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class ExportMenu
{
    [Parameter]
    public EventCallback<DateTime?> OnExport { get; set; }

    private bool _isOpen;
    private DateTime? _day = DateTime.Today;

    private void Open() => _isOpen = true;

    private void Close() => _isOpen = false;

    private Task ExportAllAsync() => ExportAsync(null);

    private Task ExportDayAsync() => ExportAsync(_day);

    private async Task ExportAsync(DateTime? day)
    {
        Close();
        await OnExport.InvokeAsync(day);
    }
}
