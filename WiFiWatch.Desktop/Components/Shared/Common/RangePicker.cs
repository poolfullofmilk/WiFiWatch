using MudBlazor;

namespace WiFiWatch.Desktop.Components.Shared.Common;

// A Date Range Picker Whose Presets Only Mark Days, OK Still Applies Them
public class RangePicker : MudDateRangePicker
{
    public async Task PickAsync(DateTime start, DateTime end)
    {
        // Two Day Clicks, As A Person Would Make Them, Which Never Submit While Actions Show
        await OnDayClickedAsync(start);
        await OnDayClickedAsync(end);

        // The Calendar Opens On The First Marked Day, Or 30 Days Would Start Out Of Sight
        PickerMonth = new DateTime(start.Year, start.Month, 1);
        StateHasChanged();
    }
}
