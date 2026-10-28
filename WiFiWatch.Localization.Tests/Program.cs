using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using WiFiWatch.Data;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;
using WiFiWatch.Desktop;
using WiFiWatch.Desktop.Components;
using WiFiWatch.Desktop.Components.Pages;
using WiFiWatch.Desktop.Components.Shared.Dialogs;
using WiFiWatch.Desktop.Localization;
using WiFiWatch.Services.Integration;
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
            await TestFullAppAsync();
            TestSourceCoverage();
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
                if (language != "en" && key != "AppName")
                    Check(
                        resources.GetResourceSet(AppText.Culture, true, false)!.GetString(key)
                            is not null,
                        "Translation present: " + language + ": " + key
                    );
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
                if (key == "MudPagerInfo")
                {
                    Check(
                        new[] { "{first_item}", "{last_item}", "{all_items}" }.All(token =>
                            AppText.Get(key).Contains(token)
                        ),
                        "Pager template preserves all named placeholders"
                    );
                    continue;
                }
                var indices = Regex
                    .Matches((string)entry.Value!, @"\{(\d+)")
                    .Select(match => int.Parse(match.Groups[1].Value))
                    .ToArray();
                object[] arguments = Enumerable
                    .Repeat<object>(
                        new FormattingProbe(),
                        indices.Length == 0 ? 0 : indices.Max() + 1
                    )
                    .ToArray();
                Check(!string.IsNullOrEmpty(AppText.Get(key, arguments)), "Format " + key);
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

    private static void TestSourceCoverage()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WiFiWatch.slnx")))
            root = root.Parent;
        Check(root is not null, "Repository found for source coverage");
        var resources = new ResourceManager(
            "WiFiWatch.Data.Localization.Strings",
            typeof(AppText).Assembly
        );
        foreach (
            var project in new[] { "WiFiWatch.Data", "WiFiWatch.Services", "WiFiWatch.Desktop" }
        )
        foreach (
            var file in Directory
                .EnumerateFiles(
                    Path.Combine(root!.FullName, project),
                    "*",
                    SearchOption.AllDirectories
                )
                .Where(file =>
                    (file.EndsWith(".cs") || file.EndsWith(".razor"))
                    && !file.Contains(
                        Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar
                    )
                    && !file.Contains(
                        Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar
                    )
                )
        )
        {
            var source = File.ReadAllText(file);
            foreach (
                Match match in Regex.Matches(
                    source,
                    @"AppText\.Get\(\s*""(?<key>(?:[^""\\]|\\.)*)"""
                )
            )
            {
                var key = Regex.Unescape(match.Groups["key"].Value);
                Check(
                    resources.GetString(key, CultureInfo.GetCultureInfo("en")) is not null,
                    "Resource key exists: " + key + " in " + file
                );
            }
            if (!file.EndsWith(".razor"))
                continue;
            source = Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline);
            var literalAttributes = Regex.Matches(
                source,
                "(?:Title|Label|Description|Text|DataLabel|PlaceHolder|SortLabel|RowsPerPageString|Name)=\"[A-Za-z][A-Za-z0-9 -]*\""
            );
            Check(literalAttributes.Count == 0, "No hardcoded human labels: " + file);
            var literalText = Regex.Matches(source, @">\s*[A-Za-z][A-Za-z .,!?'-]{2,}\s*<");
            Check(literalText.Count == 0, "No hardcoded visible text: " + file);
        }
    }

    private sealed class FormattingProbe : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => "42";
    }

    private static async Task TestFullAppAsync()
    {
        await using (var context = new WiFiDbContext())
        {
            context.MinuteSamples.Add(
                new MinuteSample
                {
                    MinuteUtc = DateTime.UtcNow.AddMinutes(-1),
                    Link = NetworkMonitor.WiFiLink,
                    Channel = 100,
                    Rssi = -55,
                    InternetLossPercent = 0,
                    InternetPingMilliseconds = 12,
                }
            );
            await context.SaveChangesAsync();
        }
        var settings = new UserSettings();
        var monitor = new NetworkMonitor(
            settings,
            false,
            subscribeToNetworkChanges: false,
            inspectAdapter: () =>
                Task.FromResult<AdapterInfo?>(
                    new AdapterInfo(
                        "Test Wi-Fi",
                        "99.0",
                        DateTime.Today,
                        "Maximum Performance",
                        "Medium Power Saving",
                        "Test roaming"
                    )
                )
        );
        var interceptor = new AppLocalizationInterceptor();
        foreach (
            var (language, overview) in new[]
            {
                ("en", "Overview"),
                ("nl", "Overzicht"),
                ("tr", "Genel Bakış"),
                ("de", "Übersicht"),
                ("es", "Resumen"),
                ("ru", "Обзор"),
            }
        )
        {
            AppText.Select(language);
            Check(AppText.Get("Overview") == overview, "Navigation " + language);
            Check(
                EventKind.PacketLoss.ToLabel() == AppText.Get("Packet Loss"),
                "Event kind " + language
            );
            foreach (var kind in Enum.GetValues<EventKind>())
                Check(
                    !string.IsNullOrEmpty(kind.ToLabel()),
                    "Event label: " + kind + " in " + language
                );
            var changed = WiFiReader.DescribeChannelChange(100, 36);
            Check(
                changed.Item1 == EventKind.DfsEviction
                    && changed.Item2 == AppText.Get("Channel {0} To {1} Left DFS", 100, 36),
                "Channel-change template " + language
            );
            Check(
                ChannelAdvice.Advise(
                    6,
                    ChannelAdvice.Blocks.ToDictionary(block => block, _ => 0),
                    0
                ) == AppText.Get("Not On A 5 GHz 80 MHz Channel"),
                "Channel advice " + language
            );
            Check(
                QuickActions.ForEvent(EventKind.LocationBlocked, NetworkMonitor.WiFiScope, null)
                    == QuickActions.LocationSettings,
                "Stable fix URI " + language
            );
            Check(
                interceptor.Handle("MudTablePager_NextPage").Value == AppText.Get("MudNext"),
                "Built-in pager " + language
            );
            Check(
                interceptor.Handle("MudInput_Clear").Value == AppText.Get("MudClear"),
                "Built-in input " + language
            );
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<CapturingActivator>();
            services.AddSingleton<IComponentActivator>(provider =>
                provider.GetRequiredService<CapturingActivator>()
            );
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddMudServices(options =>
                options.PopoverOptions.CheckForPopoverProvider = false
            );
            services.AddLocalization();
            services.AddLocalizationInterceptor<AppLocalizationInterceptor>();
            services.AddApexCharts();
            services.AddSingleton(monitor);
            services.AddSingleton(new MainWindow());
            await using var provider = services.BuildServiceProvider();
            foreach (
                var (component, expected) in new[]
                {
                    (typeof(Home), "Overview"),
                    (typeof(OverviewPage), "Recent Problems"),
                    (typeof(IncidentsPage), "Details"),
                    (typeof(HistoryPage), "Speed Tests"),
                    (typeof(SettingsPage), "General"),
                    (typeof(IncidentDialog), "Close"),
                    (typeof(SpeedTestDialog), "Test Again"),
                    (typeof(UpdateDialog), "Don't Show Again"),
                }
            )
            {
                await using var renderer = new HtmlRenderer(
                    provider,
                    provider.GetRequiredService<ILoggerFactory>()
                );
                provider.GetRequiredService<CapturingActivator>().LastDialog = null;
                var html = await renderer.Dispatcher.InvokeAsync(async () =>
                {
                    IDictionary<string, object>? parameters =
                        component == typeof(IncidentDialog)
                            ? new Dictionary<string, object>
                            {
                                ["Event"] = new WiFiEvent
                                {
                                    Kind = EventKind.PacketLoss,
                                    Message = "Legacy incident text",
                                    EndedAtUtc = DateTime.UtcNow,
                                },
                            }
                        : component == typeof(UpdateDialog)
                            ? new Dictionary<string, object> { ["Version"] = new Version(99, 0) }
                        : null;
                    var proxy = DispatchProxy.Create<IMudDialogInstance, DialogProxy>();
                    var result = await renderer.RenderComponentAsync<RenderHost>(
                        ParameterView.FromDictionary(
                            new Dictionary<string, object?>
                            {
                                ["Component"] = component,
                                ["Parameters"] = parameters,
                                ["Dialog"] = proxy,
                            }
                        )
                    );
                    if (
                        component != typeof(Home)
                        && provider.GetRequiredService<CapturingActivator>().LastDialog
                            is { } dialog
                    )
                    {
                        var fragments = await renderer.RenderComponentAsync<DialogFragments>(
                            ParameterView.FromDictionary(
                                new Dictionary<string, object?>
                                {
                                    ["Title"] = dialog.TitleContent,
                                    ["Content"] = dialog.DialogContent,
                                    ["Actions"] = dialog.DialogActions,
                                }
                            )
                        );
                        return fragments.ToHtmlString();
                    }
                    if (component == typeof(IncidentsPage))
                    {
                        var page = provider.GetRequiredService<CapturingActivator>().IncidentPage!;
                        typeof(IncidentsPage)
                            .GetField(
                                "_searchTerm",
                                BindingFlags.Instance | BindingFlags.NonPublic
                            )!
                            .SetValue(page, AppText.Get("Slow DNS"));
                        var load =
                            (Task<TableData<WiFiEvent>>)
                                typeof(IncidentsPage)
                                    .GetMethod(
                                        "LoadIncidentsAsync",
                                        BindingFlags.Instance | BindingFlags.NonPublic
                                    )!
                                    .Invoke(
                                        page,
                                        [
                                            new TableState
                                            {
                                                Page = 0,
                                                PageSize = 50,
                                                SortDirection = SortDirection.Descending,
                                            },
                                            CancellationToken.None,
                                        ]
                                    )!;
                        Check(
                            (await load).Items!.Any(row => row.Kind == EventKind.SlowDns),
                            "Localized search finds historical incidents in " + language
                        );
                    }
                    if (component == typeof(Home))
                    {
                        AppText.Select(language == "en" ? "ru" : "en");
                        Check(
                            System
                                .Net.WebUtility.HtmlDecode(result.ToHtmlString())
                                .Contains(AppText.Get("Overview")),
                            "Live navigation language switch from " + language
                        );
                        AppText.Select(language);
                    }
                    return result.ToHtmlString();
                });
                Check(
                    System.Net.WebUtility.HtmlDecode(html).Contains(AppText.Get(expected)),
                    "Rendered " + component.Name + " in " + language
                );
            }
        }
        AppText.Select("ru");
        Check(Formatter.FormatCount(21, "Problem") == "21 проблема", "Russian count ending in one");
        Check(Formatter.FormatCount(22, "Problem") == "22 проблемы", "Russian count ending in two");
        Check(Formatter.FormatCount(11, "Attempt") == "11 попыток", "Russian count teens");
        Check(Formatter.FormatDuration(TimeSpan.FromMinutes(2)) == "2мин", "Compact duration");
        Check(
            Formatter.FormatNumber(12.5, "ms") == "12.5 мс",
            "Localized units preserve Windows numbers"
        );
        var journal = new EventJournal();
        await journal.RecordAsync(
            EventKind.DriverCheck,
            EventSeverity.Warning,
            AppText.Get("Driver", "1.2"),
            details: new EventDetails(DriverVersion: "1.2")
        );
        AppText.Select("de");
        Check(
            await EventJournal.HasDriverVersionAsync("1.2"),
            "Driver deduplication survives language changes"
        );
        await journal.RecordAsync(
            EventKind.DriverCheck,
            EventSeverity.Warning,
            "Wi-Fi Driver 0.9 Dates From 2020-01-01"
        );
        Check(await EventJournal.HasDriverVersionAsync("0.9"), "Legacy driver deduplication");
        await using (var context = new WiFiDbContext())
        {
            context.MinuteSamples.Add(
                new MinuteSample
                {
                    MinuteUtc = DateTime.Today.AddDays(-1).AddHours(12).ToUniversalTime(),
                    Link = NetworkMonitor.EthernetLink,
                    InternetLossPercent = 0,
                }
            );
            await context.SaveChangesAsync();
        }
        AppText.Select("en");
        await SummaryWriter.WriteMissingAsync(journal, false);
        AppText.Select("ru");
        await SummaryWriter.WriteMissingAsync(journal, false);
        await using (var context = new WiFiDbContext())
            Check(
                await context.Events.CountAsync(row => row.Kind == EventKind.DailySummary) == 1,
                "Daily summary deduplication survives a language change"
            );
        var tracker = new ConditionTracker(journal);
        var minute = DateTime.UtcNow;
        var signal = new ConditionTracker.Check(
            "signal",
            EventKind.WeakSignal,
            "Weak Signal",
            "dBm",
            -80,
            -70,
            false,
            false,
            NetworkMonitor.WiFiScope,
            "Test",
            minute
        );
        AppText.Select("en");
        await tracker.EvaluateAsync(signal);
        await tracker.EvaluateAsync(signal with { MinuteUtc = minute.AddMinutes(1) });
        AppText.Select("ru");
        await tracker.EvaluateAsync(signal with { Value = -50, MinuteUtc = minute.AddMinutes(2) });
        await tracker.EvaluateAsync(signal with { Value = -50, MinuteUtc = minute.AddMinutes(3) });
        await using (var context = new WiFiDbContext())
        {
            var row = await context.Events.SingleAsync(row => row.Kind == EventKind.WeakSignal);
            Check(
                row.Message.StartsWith(EventKind.WeakSignal.ToLabel())
                    && row.Message.Contains("-80 дБм")
                    && !row.Message.Contains("Weak Signal"),
                "Generic incident closing uses the current language and localized units"
            );
        }
    }

    public class DialogProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            return method!.ReturnType == typeof(void) ? null
                : method.ReturnType == typeof(Task) ? Task.CompletedTask
                : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType)
                : null;
        }
    }

    private sealed class CapturingActivator(IServiceProvider services) : IComponentActivator
    {
        public MudDialog? LastDialog { get; set; }
        public IncidentsPage? IncidentPage { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)ActivatorUtilities.CreateInstance(services, componentType);
            if (component is MudDialog dialog)
                LastDialog = dialog;
            if (component is IncidentsPage incidentPage)
                IncidentPage = incidentPage;
            return component;
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() =>
            Initialize("https://example.invalid/", "https://example.invalid/");

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Render tests must not navigate");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(default(TValue)!);
    }
}
