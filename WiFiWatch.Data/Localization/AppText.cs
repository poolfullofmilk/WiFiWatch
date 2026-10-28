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

    public static string SlowDnsSummary(TimeSpan duration, double peak, double average)
    {
        // One Language Snapshot Keeps A Switch From Mixing Duration And Message Languages
        var culture = Culture;
        return Format(culture, "SlowDnsSummary", FormatDuration(duration, culture), peak, average);
    }

    public static string FormatDuration(TimeSpan duration) => FormatDuration(duration, Culture);

    private static string FormatDuration(TimeSpan duration, CultureInfo culture)
    {
        var seconds = Math.Max(0, (int)duration.TotalSeconds);
        var minutes = seconds / 60;
        return minutes == 0 ? Count(seconds, "Second", culture)
            : seconds % 60 == 0 ? Count(minutes, "Minute", culture)
            : $"{Count(minutes, "Minute", culture)} {Count(seconds % 60, "Second", culture)}";
    }

    public static string FormatCount(int count, string noun)
    {
        var key = noun switch
        {
            "Problem" => "CountProblem",
            "Attempt" => "CountAttempt",
            "Neighbor" => "CountNeighbor",
            "Speed Test" => "CountSpeedTest",
            "DFS Eviction" => "CountDfsEviction",
            "Radar Eviction" => "CountRadarEviction",
            _ => null,
        };
        return key is null
            ? $"{count} {noun}{(count == 1 ? string.Empty : "s")}"
            : Count(count, key, Culture);
    }

    private static string Count(int count, string unit, CultureInfo culture)
    {
        var form = count == 1 ? "One" : "Many";
        if (culture.TwoLetterISOLanguageName == "ru")
        {
            form =
                count % 100 is >= 11 and <= 14 ? "Many"
                : count % 10 == 1 ? "One"
                : count % 10 is >= 2 and <= 4 ? "Few"
                : "Many";
        }
        return Format(culture, unit + form, count);
    }
}
