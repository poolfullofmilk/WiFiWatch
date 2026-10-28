# First localization patch (issue #1)

Settings and new Slow DNS messages support English, Dutch, Turkish, German,
Spanish and Russian. This is intentionally a first slice, not a translation of
all screens. English lives in `WiFiWatch.Data/Localization/Strings.resx`; the five
satellite `.resx` files use identical placeholder indices. `AppText` uses the
standard .NET `ResourceManager`, without adding a localization dependency or DI
service. `App.xaml.cs` initializes it before starting the monitor and WebView.

## Behavior and compatibility

- `UserSettings.Language` is nullable. Missing/null/empty means the Windows user
  UI culture captured at startup (`CultureInfo.CurrentUICulture`). Explicit
  supported cultures override it; unsupported or invalid cultures use English.
  Region-specific cultures inherit their language resources. Returning to
  automatic uses the captured Windows culture; a Windows language change needs
  an app restart.
- The language selector persists immediately and refreshes Settings. Explicit
  resource lookups see the new selection in existing background monitor tasks,
  without relying on their inherited thread culture.
- Missing translated keys fall back to the neutral English resources. `AppName`
  deliberately exists only in the neutral file and exercises this behavior.
  Completely unknown keys display their key rather than crashing.
- Old JSON remains case-insensitive, ignores removed keys, preserves existing
  values and supplies defaults for omitted properties. Tests set the
  `WiFiWatch.TestDirectory` AppContext override before loading settings or
  opening a database, so the production storage APIs are exercised
  without touching real data.
- Slow DNS opening, escalation and closing messages use resource templates and
  numeric placeholders that keep the Windows number format independently of
  the selected UI language. One language snapshot formats each closing message
  and its duration together. Russian durations use one/few/many forms,
  including 11–14 and 21/22. Other event kinds retain their existing formatting.
- No schema change or history rewrite is made. `EventJournal` still stores plain
  message text. Completed historical messages retain their original wording and
  language after a switch. An active incident's next update/close can be written
  in the newly selected language. Existing journal suffixes, scopes, toast titles,
  advice, navigation and other pages/dialogs remain English. Windows-provided
  adapter values and disconnect reasons retain the Windows language.
- `SatelliteResourceLanguages` includes `en;nl;tr;de;es;ru`.
  `VerifyLocalizationBundle` fails publication if any of the five Data satellite
  assemblies is absent from `FilesToBundle`. English is embedded in Data itself.
  The release workflow runs the regression executable before publishing.

## Reproduce

.NET 10 SDK, from the repository root:

```sh
dotnet build WiFiWatch.slnx -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true
dotnet run --project WiFiWatch.Localization.Tests/WiFiWatch.Localization.Tests.csproj --no-build
dotnet run --project WiFiWatch.Localization.Tests/WiFiWatch.Localization.Tests.csproj -c Release -p:UseSharedCompilation=false -p:BuildInParallel=false
dotnet publish WiFiWatch.Desktop/WiFiWatch.Desktop.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true
```

The regression executable exits nonzero on failure. It covers legacy and damaged
settings, all six persisted choices, Windows defaults, invalid/unsupported
languages, regional inheritance, English key fallback, every template's
placeholder indices and typed formatting, background selection changes, Russian
duration forms and Windows/custom number formats. The production migrations,
ConditionTracker and EventJournal open, escalate, notify and close an incident
in a temporary SQLite database, including a language switch while it is open.
Closed historical text and legacy details remain unchanged across all six
languages and the startup leftover-incident handler. The temporary
settings and SQLite directory is deleted in `finally`; no production database
or settings are opened.

## Observed locally, 2026-10-28

Ubuntu, .NET SDK 10.0.403:

- Full solution Debug build: succeeded, 0 warnings, 0 errors.
- Debug and Release regression executables: PASS, 877 assertions each.
- Release publication: succeeded; `Verified localization bundle: nl;tr;de;es;ru`.
  Output: the single `WiFiWatch_v1.9.exe` (approximately 82 MiB).
- CSharpier 1.3.0 check on changed C# and project files: succeeded.
- `git diff --check`: succeeded.
- `dotnet format analyzers` and `dotnet format style`, `--verify-no-changes
  --severity info --no-restore`, attempted separately for Data, Services and
  Desktop: all blocked before analysis by the build-host named pipe failing with
  `SocketException (13): Permission denied` (host exit 137). No analyzer/style
  success is claimed. Initial parallel MSBuild attempts failed in this container;
  the single-worker commands above completed.

## Windows checks still required

These could not be run on this Linux host:

- Actual Windows user-culture discovery on Russian/Dutch/etc. Windows and an
  installed executable restart retaining the chosen language.
- WPF startup/tray notifications and a standalone published EXE launch/extraction.
- BlazorWebView/MudSelect interaction, immediate rerendering, text wrapping and
  numeric fields under all six languages, and updated Settings screenshots.
- Live network measurement and Windows toast delivery. The tracker/journal
  lifecycle and notification callback are covered headlessly.
- The installed self-updater and SignPath signing. A successful cross-build and
  bundle-input check do not establish these runtime results.

## Translation review

All 43 Russian localized values in `Strings.ru.resx` are provisional and need
wporgru's review: Settings headings, switches, descriptions and thresholds;
adapter/update templates; language-picker text; `SlowDnsValue`, `SlowDnsSummary`;
and `MinuteOne/Few/Many`, `SecondOne/Few/Many` (including grammatical case in the
message). No Russian text is attributed to wporgru yet.

Translations still to be supplied in a later slice include the shell/tray,
other pages/dialogs, Band Change, Radar Eviction, Ping Spike, Packet Loss, Weak
Signal, Daily Summary, advice and journal suffixes. The Dutch, Turkish, German
and Spanish translations are also initial translations requiring native review.
