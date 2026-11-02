using Microsoft.AspNetCore.Components;
using WiFiWatch.Data.Localization;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public partial class LanguagePicker
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    // Null Follows Windows, The Rest Sorted By Their Own Name
    private static IEnumerable<(string? Language, string Name)> Choices =>
        [
            (null, AppText.Get("SystemLanguage")),
            .. AppText
                .Languages.OrderBy(language => language.Value, StringComparer.Ordinal)
                .Select(language => ((string?)language.Key, language.Value)),
        ];

    private void Select(string? language)
    {
        // Saved Before The Switch, Which Redraws Every Page At Once
        Monitor.Settings = Monitor.Settings with
        {
            Language = language,
        };
        Monitor.Settings.Save();
        AppText.Select(language);
    }
}
