using MudBlazor;

namespace WiFiWatch.Desktop.Theming;

public static class GradeColors
{
    public static Color For(string grade) =>
        grade switch
        {
            "A+" or "A" => Color.Success,
            "B" or "C" => Color.Warning,
            _ => Color.Error,
        };
}
