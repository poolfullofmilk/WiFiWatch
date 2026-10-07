namespace WifiWatch.Data.ViewModels;

public sealed record AdapterInfo(
    string Name,
    string? DriverVersion,
    DateTime? DriverDate,
    string? PowerSavingOnPower,
    string? PowerSavingOnBattery,
    string? Roaming
)
{
    // Wi-Fi Drivers Older Than This Missed Real Fixes
    private const int OldDriverMonths = 18;

    public bool IsDriverOld => DriverDate < DateTime.Today.AddMonths(-OldDriverMonths);
}
