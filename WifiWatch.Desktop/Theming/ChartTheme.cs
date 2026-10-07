using ApexCharts;
using MudBlazor.Utilities;

namespace WifiWatch.Desktop.Theming;

public static class ChartTheme
{
    // The Lightest Grey, Shaded Down Per Bar From Here
    public const string LightGreyColor = "#BDBDBD";
    public const string DarkGreyColor = "#757575";

    // Ten Bars Still Clear The Dark Panel At This Spread
    private const double MonochromeShadeIntensity = 0.45;

    // The One Accent From The App Palette
    public static readonly string AccentColor = Hex(AppTheme.Custom.PaletteDark.Info);

    public static XAxisLabels FlatLabels() => new() { Rotate = 0, HideOverlappingLabels = true };

    private static string Hex(MudColor color) => color.ToString(MudColorOutputFormats.Hex);

    public static ApexChartOptions<TPoint> BuildBaseOptions<TPoint>()
        where TPoint : class =>
        new()
        {
            // Grey Shades Replace The Palette Entirely
            Theme = new Theme
            {
                Mode = Mode.Dark,
                Monochrome = new ThemeMonochrome
                {
                    Enabled = true,
                    Color = LightGreyColor,
                    ShadeTo = Mode.Dark,
                    ShadeIntensity = MonochromeShadeIntensity,
                },
            },
            Chart = new Chart
            {
                FontFamily = string.Join(", ", AppTheme.FontFamily),
                Toolbar = new Toolbar { Show = false },
                Zoom = new Zoom { Enabled = false },
                Selection = new Selection { Enabled = false },
                Animations = new Animations { Enabled = false },
            },
            // Remove The Click Highlight So Bars Stay Non-Interactive
            States = new States
            {
                Active = new StatesActive
                {
                    AllowMultipleDataPointsSelection = false,
                    Filter = new StatesFilter { Type = StatesFilterType.none },
                },
            },
            DataLabels = new DataLabels { Enabled = false },
        };
}
