using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WiFiWatch.Data;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Localization.Tests;

internal static class Program
{
    private static int s_checks;

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WiFiWatch-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        AppContext.SetData("WiFiWatch.TestDirectory", directory);
        var numberCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Check(WiFiDbContext.DataDirectory == directory, "All stores are redirected to temp");
            TestSettings(Path.Combine(directory, "settings.json"));
            await TestResourcesAsync();
            await TestIncidentsAsync();
            Console.WriteLine($"PASS: {s_checks} localization regression checks");
        }
        finally
        {
            AppText.Select("en");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
            CultureInfo.CurrentCulture = numberCulture;
            AppContext.SetData("WiFiWatch.TestDirectory", null);
        }
    }

    private static void Check(bool passed, string name)
    {
        if (!passed)
            throw new InvalidOperationException(name);
        s_checks++;
    }

    private static void TestSettings(string path)
    {
        Check(UserSettings.Load().Language is null, "Missing file uses Windows language");
        File.WriteAllText(
            path,
            "{\"ReadWifiNatively\":true,\"RouterPingMilliseconds\":37,\"RemovedKey\":1}"
        );
        var settings = UserSettings.Load();
        Check(
            settings.Language is null
                && settings.ReadWiFiNatively
                && settings.RouterPingMilliseconds == 37,
            "Old settings preserve values and case-insensitive keys"
        );
        Check(
            settings.WeakSignalRssi == -70 && settings.ShowUpdatePopup,
            "Missing keys retain defaults"
        );
        foreach (var language in AppText.Languages.Keys)
        {
            (settings with { Language = language }).Save();
            Check(UserSettings.Load().Language == language, "Persist " + language);
        }
        (settings with { Language = null }).Save();
        Check(UserSettings.Load().Language is null, "Persist automatic selection");
        File.WriteAllText(path, "broken JSON");
        Check(UserSettings.Load() == new UserSettings(), "Damaged settings use defaults");
        Check(
            AppText.Resolve(null, CultureInfo.GetCultureInfo("ru-RU")).Name == "ru-RU",
            "Windows Russian default"
        );
        Check(
            AppText.Resolve("nl", CultureInfo.GetCultureInfo("ru-RU")).Name == "nl",
            "Explicit selection wins"
        );
        Check(
            AppText.Resolve("", CultureInfo.GetCultureInfo("de-DE")).Name == "de-DE",
            "Automatic selection"
        );
        Check(
            AppText.Resolve(null, CultureInfo.GetCultureInfo("fr-FR")).Name == "en",
            "Unsupported Windows language"
        );
        Check(
            AppText.Resolve("not_a_culture!", CultureInfo.InvariantCulture).Name == "en",
            "Invalid saved culture"
        );
        Check(
            AppText.Resolve("fr", CultureInfo.InvariantCulture).Name == "en",
            "Unsupported saved culture"
        );
    }

    private static async Task TestResourcesAsync()
    {
        var resources = new ResourceManager(
            "WiFiWatch.Data.Localization.Strings",
            typeof(AppText).Assembly
        );
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)!;
        foreach (var language in AppText.Languages.Keys)
        {
            AppText.Initialize(language, CultureInfo.GetCultureInfo("ru-RU"));
            if (language != "en")
                Check(
                    resources.GetResourceSet(AppText.Culture, true, false) is not null,
                    "Satellite exists: " + language
                );
            foreach (System.Collections.DictionaryEntry entry in english)
            {
                var key = (string)entry.Key;
                var template = resources.GetString(key, AppText.Culture)!;
                Check(!string.IsNullOrEmpty(template), language + ": " + key);
                var expectedIndices = Regex
                    .Matches((string)entry.Value!, @"\{(\d+)")
                    .Select(match => match.Groups[1].Value)
                    .Order();
                var actualIndices = Regex
                    .Matches(template, @"\{(\d+)")
                    .Select(match => match.Groups[1].Value)
                    .Order();
                Check(
                    expectedIndices.SequenceEqual(actualIndices),
                    "Placeholder parity: " + language + ": " + key
                );
                // Every Template Must Accept The Same Typed Dynamic Values
                object[] arguments =
                    key == "DriverFrom" ? ["1.2", new DateTime(2026, 10, 28)]
                    : key == "SlowDnsSummary" ? ["duration", 12.5, 8.5]
                    : key == "SlowDnsValue" ? [12.5]
                    : key.StartsWith("Minute") || key.StartsWith("Second") ? [21]
                    : ["1.2"];
                Check(
                    !string.IsNullOrEmpty(
                        string.Format(CultureInfo.CurrentCulture, template, arguments)
                    ),
                    "Format " + key
                );
            }
            Check(
                AppText.Get("AppName") == "Wi-Fi Watch",
                "Missing satellite key falls back to English"
            );
            Check(
                AppText.Get("General") != "General" || language == "en" || language == "es",
                "Localized page heading"
            );
        }
        AppText.Select("ru-RU");
        Check(AppText.Get("General") == "Общие", "Regional culture inherits Russian resources");
        Check(
            AppText.Get("SlowDnsValue", 12.5) == "Медленный DNS: 12.5 мс",
            "Russian UI keeps Windows decimal formatting"
        );
        var numberCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
        AppText.Select("en");
        Check(
            AppText.Get("SlowDnsValue", 12.5) == "Slow DNS 12,5 ms",
            "English UI keeps Dutch Windows number format"
        );
        var customCulture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        customCulture.NumberFormat.NumberDecimalSeparator = "·";
        CultureInfo.CurrentCulture = customCulture;
        AppText.Select("ru");
        Check(
            AppText.Get("SlowDnsValue", 12.5) == "Медленный DNS: 12·5 мс",
            "Custom Windows number format"
        );
        CultureInfo.CurrentCulture = numberCulture;
        foreach (
            var (count, word) in new[]
            {
                (1, "минуту"),
                (2, "минуты"),
                (5, "минут"),
                (11, "минут"),
                (14, "минут"),
                (21, "минуту"),
                (22, "минуты"),
                (25, "минут"),
                (111, "минут"),
                (121, "минуту"),
            }
        )
            Check(
                AppText.FormatDuration(TimeSpan.FromMinutes(count)) == $"{count} {word}",
                "Russian minutes " + count
            );
        foreach (
            var (count, word) in new[]
            {
                (0, "секунд"),
                (1, "секунду"),
                (2, "секунды"),
                (5, "секунд"),
                (11, "секунд"),
                (21, "секунду"),
                (22, "секунды"),
            }
        )
            Check(
                AppText.FormatDuration(TimeSpan.FromSeconds(count)) == $"{count} {word}",
                "Russian seconds " + count
            );
        Check(
            AppText.FormatDuration(TimeSpan.FromSeconds(125)) == "2 минуты 5 секунд",
            "Combined duration"
        );
        Check(AppText.FormatDuration(TimeSpan.FromSeconds(-1)) == "0 секунд", "Negative duration");
        var ready = new TaskCompletionSource();
        var resume = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            ready.SetResult();
            await resume.Task;
            return AppText.Get("General");
        });
        await ready.Task;
        AppText.Select("de");
        resume.SetResult();
        Check(await background == "Allgemein", "Existing monitor tasks see a changed language");
        AppText.Select(null);
        Check(AppText.Culture.Name == "ru-RU", "Return to captured Windows culture");
        AppText.Select("fr");
        Check(AppText.Get("General") == "General", "Unsupported language falls back to English");
        Check(AppText.Get("UnknownKey") == "UnknownKey", "Unknown key stays readable");
    }

    private static async Task TestIncidentsAsync()
    {
        // Real Migrations, Tracker And Journal, All Inside The Temporary Data Directory
        const string oldMessage = "Slow DNS For 12m, Peak 400 ms, Average 300 ms";
        var start = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
        int historyId;
        await using (var context = new WiFiDbContext())
        {
            await context.Database.MigrateAsync();
            var history = new WiFiEvent
            {
                Message = oldMessage,
                Details = "Legacy context",
                OccurredAtUtc = start.AddDays(-1),
                EndedAtUtc = start.AddDays(-1).AddMinutes(12),
                Kind = EventKind.SlowDns,
                Severity = EventSeverity.Warning,
            };
            context.Events.Add(history);
            await context.SaveChangesAsync();
            historyId = history.Id;
        }
        AppText.Select("en");
        var journal = new EventJournal();
        var tracker = new ConditionTracker(journal);
        string? notification = null;
        journal.Alerted += (_, message) => notification = message;
        var check = new ConditionTracker.Check(
            "dns",
            EventKind.SlowDns,
            "Slow DNS",
            "ms",
            200,
            150,
            true,
            false,
            "DNS",
            "Test resolver",
            start
        );
        await tracker.EvaluateAsync(check);
        Check(!journal.IsOpen("dns"), "One bad minute does not open a warning");
        await tracker.EvaluateAsync(check with { Value = 300, MinuteUtc = start.AddMinutes(1) });
        Check(journal.IsOpen("dns"), "Second bad minute opens an incident");
        var id = journal.OpenIdOf("dns")!.Value;
        await using (var context = new WiFiDbContext())
        {
            var opened = await context.Events.SingleAsync(row => row.Id == id);
            Check(
                opened.Message == "Slow DNS 300 ms" && opened.EndedAtUtc is null,
                "English opening stored in SQLite"
            );
            Check(
                EventDetails.Parse(opened.Details).Context == "Test resolver",
                "Dynamic context preserved"
            );
        }
        AppText.Select("ru");
        await using (var context = new WiFiDbContext())
            Check(
                (await context.Events.SingleAsync(row => row.Id == id)).Message
                    == "Slow DNS 300 ms",
                "Language switch alone does not rewrite an open incident"
            );
        await tracker.EvaluateAsync(check with { Value = 600.5, MinuteUtc = start.AddMinutes(2) });
        await using (var context = new WiFiDbContext())
        {
            var updated = await context.Events.SingleAsync(row => row.Id == id);
            Check(
                updated.Message == "Медленный DNS: 600.5 мс"
                    && updated.Severity == EventSeverity.Critical,
                "Russian escalation stored in SQLite"
            );
            Check(notification == updated.Message, "Notification carries the localized message");
        }
        await tracker.EvaluateAsync(check with { Value = 100, MinuteUtc = start.AddMinutes(3) });
        Check(journal.IsOpen("dns"), "One good minute leaves the incident open");
        await tracker.EvaluateAsync(check with { Value = 100, MinuteUtc = start.AddMinutes(4) });
        Check(!journal.IsOpen("dns"), "Second good minute closes the incident");
        await using (var context = new WiFiDbContext())
        {
            var closed = await context.Events.SingleAsync(row => row.Id == id);
            Check(
                closed.Message
                    == "Медленный DNS наблюдался 3 минуты, пик 600.5 мс, среднее 366.8 мс",
                "Localized duration, peak and average stored on close"
            );
            Check(
                closed.EndedAtUtc == start.AddMinutes(3),
                "Incident end time unchanged by localization"
            );
        }
        await EventJournal.CloseLeftoversAsync();
        foreach (var language in AppText.Languages.Keys)
        {
            AppText.Select(language);
            await using var context = new WiFiDbContext();
            var history = await context.Events.SingleAsync(row => row.Id == historyId);
            Check(
                history.Message == oldMessage
                    && history.Details == "Legacy context"
                    && history.EndedAtUtc == start.AddDays(-1).AddMinutes(12),
                "Historical message and metadata preserved in " + language
            );
            Check(
                EventDetails.Parse(history.Details).Context == "Legacy context",
                "Legacy plain-text details remain readable"
            );
        }
    }
}
