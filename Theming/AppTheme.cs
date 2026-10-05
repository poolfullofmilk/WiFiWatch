using MudBlazor;

namespace WifiWatch.Theming;

public static class AppTheme
{
    // Application Font First, System Fonts Until It Loads
    public static readonly string[] FontFamily =
    [
        "Nunito",
        "Segoe UI",
        "Roboto",
        "Helvetica",
        "Arial",
    ];

    // Three Weight Steps Across The Whole App
    private const string LabelWeight = "500";
    private const string HeadingWeight = "700";
    private const string NormalLetterSpacing = "normal";

    // Shared Dark Surfaces, Bars Sit Darkest
    private const string ChromeSurface = "#141414";
    private const string PageBackground = "#202020";
    private const string RaisedSurface = "#2a2a2a";

    // One Accent Blue For Secondary And Info
    private const string AccentBlue = "#3d8bfd";

    // Shared White Overlay Steps
    private const string TextStrong = "rgba(255,255,255, 0.85)";
    private const string TextMuted = "rgba(255,255,255, 0.80)";
    private const string TextFaint = "rgba(255,255,255, 0.35)";
    private const string LineStrong = "rgba(255,255,255, 0.15)";
    private const string LineFaint = "rgba(255,255,255, 0.10)";
    private const string LineHairline = "rgba(255,255,255, 0.06)";

    // Two Depths, A Slight Lift And A Ringed Panel Drop
    private const string SlightShadow = "0 2px 8px rgba(0,0,0, 0.25)";
    private const string PanelShadow =
        $"0 0 0 1px {LineHairline}, 0 8px 24px -10px rgba(0,0,0, 0.6)";

    public static readonly MudTheme Custom = new()
    {
        PaletteDark = new PaletteDark
        {
            // Core Colors
            Primary = Colors.Gray.Lighten1,
            Secondary = AccentBlue,
            Tertiary = Colors.Gray.Default,

            // Status Colors
            Info = AccentBlue,
            Warning = "#f5b544",

            // UI Elements
            AppbarBackground = ChromeSurface,
            AppbarText = TextStrong,
            DrawerBackground = ChromeSurface,
            DrawerText = TextMuted,
            DrawerIcon = TextMuted,

            // Content Areas
            Background = PageBackground,
            Surface = RaisedSurface,
            BackgroundGray = PageBackground,

            // Text Colors
            TextPrimary = TextStrong,
            TextSecondary = "rgba(255,255,255, 0.60)",
            TextDisabled = TextFaint,

            // Link Colors
            HoverOpacity = 0.12,

            // Form Elements
            PrimaryDarken = Colors.Gray.Darken2,
            PrimaryLighten = Colors.Gray.Lighten4,
            SecondaryDarken = Colors.Gray.Darken1,
            SecondaryLighten = Colors.Shades.White,
            TertiaryDarken = Colors.Gray.Darken3,
            TertiaryLighten = Colors.Gray.Lighten2,

            // Lines And Dividers
            Divider = LineStrong,
            DividerLight = LineFaint,
            LinesDefault = LineStrong,
            LinesInputs = TextFaint,

            // Focus States
            Dark = RaisedSurface,
            DarkLighten = Colors.Gray.Darken3,
            DarkDarken = PageBackground,

            // Tables
            TableLines = LineStrong,
            TableStriped = "rgba(255,255,255, 0.05)",
            TableHover = LineFaint,

            // System
            OverlayDark = "rgba(0, 0, 0, 0.5)",
            OverlayLight = "rgba(255, 255, 255, 0.15)",
            Black = "#000000",
            White = "#ffffff",
            GrayDefault = Colors.Gray.Default,
            GrayLight = Colors.Gray.Lighten2,
            GrayLighter = Colors.Gray.Lighten4,
            GrayDark = Colors.Gray.Darken2,
            GrayDarker = Colors.Gray.Darken4,
            Skeleton = LineFaint,
        },
        Shadows = new Shadow
        {
            // Level One Lifts Chips And Cards, The Rest Are Panels
            Elevation = ["none", SlightShadow, .. Enumerable.Repeat(PanelShadow, 24)],
        },
        Typography = new Typography
        {
            // Apply The Font To Every Variant
            Default = new DefaultTypography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
            },
            H1 = new H1Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            H2 = new H2Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            H3 = new H3Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            H4 = new H4Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            H5 = new H5Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            H6 = new H6Typography
            {
                FontFamily = FontFamily,
                FontWeight = HeadingWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            Subtitle1 = new Subtitle1Typography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = FontFamily,
                FontWeight = LabelWeight,
                LetterSpacing = NormalLetterSpacing,
            },
            Body1 = new Body1Typography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
            },
            Body2 = new Body2Typography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
            },
            Button = new ButtonTypography
            {
                FontFamily = FontFamily,
                FontWeight = LabelWeight,
                LetterSpacing = NormalLetterSpacing,
                TextTransform = "none",
            },
            Caption = new CaptionTypography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
            },
            Overline = new OverlineTypography
            {
                FontFamily = FontFamily,
                LetterSpacing = NormalLetterSpacing,
                TextTransform = "none",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "15px",
            AppbarHeight = "55px",
        },
    };
}
