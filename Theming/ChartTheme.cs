using ApexCharts;

namespace WifiWatch.Theming;

public static class ChartTheme
{
    // The Lightest Grey, Shaded Down Per Bar From Here
    private const string MonochromeBaseColor = "#BDBDBD";

    // Ten Bars Still Clear The Dark Panel At This Spread
    private const double MonochromeShadeIntensity = 0.45;

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
                    Color = MonochromeBaseColor,
                    ShadeTo = Mode.Dark,
                    ShadeIntensity = MonochromeShadeIntensity,
                },
            },
            Chart = new Chart
            {
                FontFamily = string.Join(", ", AppTheme.FontFamily),
                Toolbar = new Toolbar { Show = false },
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
