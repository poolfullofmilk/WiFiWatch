using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using WiFiWatch.Data.Localization;

namespace WiFiWatch.Desktop.Localization;

// MudBlazor Must Read The Same Culture As The App, Including On WPF's Dispatcher
public sealed class AppLocalizationInterceptor : ILocalizationInterceptor
{
    private static readonly ResourceManager s_english = new(
        "MudBlazor.Resources.LanguageResource",
        typeof(MudComponentBase).Assembly
    );

    public LocalizedString Handle(string key, params object[] arguments)
    {
        var english = s_english.GetString(key, CultureInfo.InvariantCulture) ?? key;
        var resource = english switch
        {
            "First page" => "MudFirst",
            "Last page" => "MudLast",
            "Next page" => "MudNext",
            "Previous page" => "MudPrevious",
            "Clear" => "MudClear",
            "Open" => "Open",
            "Close" => "Close",
            "Start" => "Start",
            "End" => "End",
            "Select all" => "All",
            _ => english,
        };
        return new LocalizedString(key, AppText.Get(resource, arguments), resourceNotFound: false);
    }
}
