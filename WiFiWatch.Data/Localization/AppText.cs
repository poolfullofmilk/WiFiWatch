using System.Globalization;
using System.Resources;

namespace WiFiWatch.Data.Localization;

public static class AppText
{
    private static readonly ResourceManager s_resources = new(
        "WiFiWatch.Data.Localization.Strings",
        typeof(AppText).Assembly
    );
    private static CultureInfo s_culture = CultureInfo.GetCultureInfo("en");
    private static CultureInfo s_systemCulture = CultureInfo.CurrentUICulture;

    public static IReadOnlyDictionary<string, string> Languages { get; } =
        new Dictionary<string, string>
        {
            ["en"] = "English",
            ["nl"] = "Nederlands",
            ["tr"] = "Türkçe",
            ["de"] = "Deutsch",
            ["es"] = "Español",
            ["ru"] = "Русский",
        };

    // Explicit Lookups Also Reach Monitor Tasks Already Running In Another Culture
    public static CultureInfo Culture => Volatile.Read(ref s_culture);

    public static CultureInfo Resolve(string? language, CultureInfo systemCulture)
    {
        if (string.IsNullOrWhiteSpace(language))
            language = systemCulture.Name;
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            return Languages.ContainsKey(culture.TwoLetterISOLanguageName)
                ? culture
                : CultureInfo.GetCultureInfo("en");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en");
        }
    }

    public static void Initialize(string? language, CultureInfo systemCulture)
    {
        s_systemCulture = systemCulture;
        Select(language);
    }

    public static event Action? Changed;

    public static void Select(string? language)
    {
        var culture = Resolve(language, s_systemCulture);
        if (Interlocked.Exchange(ref s_culture, culture).Name != culture.Name)
            Changed?.Invoke();
    }

    public static string Get(string key, params object?[] arguments) =>
        Format(Culture, key, arguments);

    public static string Format(CultureInfo culture, string key, params object?[] arguments)
    {
        var template = s_resources.GetString(key, culture) ?? key;
        return arguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }

    // Counts Read Their Plural Form From The Key, Russian Has Three
    public static string FormatCount(int count, string key)
    {
        var form = count == 1 ? "One" : "Many";
        if (Culture.TwoLetterISOLanguageName == "ru")
        {
            form =
                count % 100 is >= 11 and <= 14 ? "Many"
                : count % 10 == 1 ? "One"
                : count % 10 is >= 2 and <= 4 ? "Few"
                : "Many";
        }
        return Get(key + form, count);
    }
}
