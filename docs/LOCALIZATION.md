# App Localization

Version 1.10 extends the initial v1.9 Settings/Slow DNS implementation to the
whole app in English, Dutch, Turkish, German, Spanish and Russian.

## Coverage

| Surface | Included |
|---|---|
| Navigation and shell | Page buttons, close prompt, tray menu, tray status and notification titles |
| Overview | Verdicts, live readings, advice, actions, recent incidents and health tooltips |
| Incidents | Filters, search hints, columns, localized kind/scope search, details and trace tables |
| History | Presets, date pickers, summary tiles, chart legends/units/locales, channels and speed tests |
| Settings | Language choice, switches, limits, adapter/power labels, About and updates |
| Dialogs | Incident details, speed tests, update prompt and errors |
| Shared controls | Pager text, accessibility labels, input errors, tooltips, export UI and bootstrap error text |
| Monitoring | Startup/resume, connection and band/channel changes, DFS/radar, thresholds, outages, DNS, traces, drivers, summaries and failures |
| Export | Headers, labels, severities, scopes, file descriptions and formatted durations |

Network names, host names, addresses, version numbers, standardized band IDs and
Windows-provided reason/roaming text are data and remain as supplied. The app
name stays Wi-Fi Watch. Stored incident messages retain the language in which
they were recorded: changing language never rewrites history. New incident
updates and closing summaries use the current language.

## Behavior

- `UserSettings.Language` persists in `settings.json`. Null/missing/empty uses the
  Windows user UI culture captured at startup. Regional variants inherit their
  language; invalid or unsupported choices fall back to English. Old settings
  retain their values and case-insensitive compatibility.
- `AppText` resolves resources explicitly, so WPF dispatchers and existing monitor
  tasks see the same selection. Its change event refreshes the navigation and
  recreates the current page, updates the tray, HTML language and error text.
  Page selection is retained. Settings save before broadcasting the change.
- English is neutral `Strings.resx`; the five satellites have 385 translated
  entries each. Missing keys inherit English. `AppName` is deliberately neutral
  only and tests this fallback.
- Numeric values retain the Windows number format. Human month/day names follow
  the UI language; fixed numeric date formats stay unchanged. Russian count and
  duration resources handle one/few/many, including 11–14 and 21/22.
- MudBlazor's localization interceptor uses the same app culture for built-in
  controls. Date pickers and chart locales also use the selected language.
- Canonical link/scope/kind values remain stable in storage and comparisons.
  Driver-alert deduplication uses optional structured `DriverVersion` details,
  with compatibility for old English messages. Daily-summary deduplication uses
  its stable numeric date prefix rather than the translated weekday.
- netsh field names, powercfg and PowerShell output, registry values and
  Windows event fields are protocol text, not UI text. They stay untranslated
  constants (`WiFiReader` holds the netsh ones) and follow the Windows language.
  `TestNetshParsingIgnoresLanguage` parses netsh samples in all six app
  languages, and a source check keeps `AppText` out of `WiFiReader.cs`.
- No database schema changes or history migrations are introduced.
- Publication checks require all five Data satellite assemblies in the single
  EXE bundle. The Windows release workflow runs the regressions before publish.

## Fixed In 2

v1.10 looked up the netsh labels `Signal`, `Band` and `Channel` through
`AppText`, so with any app language but English the parser searched netsh's
English output for `Kanaal`, `Канал` and so on. Channel was always null (no DFS
state, channel changes, evictions or returns), band was null except in German,
the neighbor scan returned nothing (no neighbors, own airtime or channel advice)
and minute rows stored no channel or band. The Debug self-test also failed when
a non-English language was saved. Those minutes cannot be recovered. The labels
are constants again and the channel change message moved to `WiFiChannels`.
Against v1.10 the new test fails for nl, tr, de, es and ru; with the fix it
passes in all six.

## Known Limits

A non-English Windows still prints netsh, powercfg and adapter property labels
in its own language, so channel, band, neighbors, power saving and roaming stay
empty there, as before v1.10. Read Wi-Fi Natively would avoid netsh for the
current connection, but `WlanQueryInterface` is denied inside the app process
and the neighbor scan always uses netsh, so the Settings line saying it works in
any language overpromises today. Options for a later release: native
`WlanGetNetworkBssList` plus solving the in-process location consent (cleanest,
largest), per-language label tables collected from real output (fragile), or
position-based parsing (not recommended, field order changes between builds).
Windows disconnect reasons and historical messages stay in the language they
were recorded in.

## Reproduce

Run from the repository root with the .NET 10 SDK:

```sh
dotnet build WiFiWatch.slnx -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true
dotnet run --project WiFiWatch.Localization.Tests/WiFiWatch.Localization.Tests.csproj --no-build
dotnet run --project WiFiWatch.Localization.Tests/WiFiWatch.Localization.Tests.csproj -c Release -p:UseSharedCompilation=false -p:BuildInParallel=false
dotnet publish WiFiWatch.Desktop/WiFiWatch.Desktop.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true
```

Tests set `WiFiWatch.TestDirectory` before opening any store. They use temporary
settings, migrated SQLite data and a synthetic adapter. Network monitoring and
native window/JavaScript operations are disabled at the test boundaries; no
live probes, adapter inspection, updater or real settings/database are used.

The harness links the production Razor components, component code, themes and
MudBlazor interceptor. `HtmlRenderer` checks navigation, all four pages and the
three dialogs in all six languages (48 cases), plus live navigation language
changes. Dialog content fragments render without native dialog interaction.

Other checks cover every satellite entry and placeholder set, English fallback,
regional/invalid cultures, legacy settings, Windows/custom number formats,
Russian plurals, localized units and event labels, actual incident lifecycle
and notification callbacks, history preservation, driver/daily-summary
deduplication, resource-key references and hardcoded Razor labels/text.

## Observed Results

Local Ubuntu, .NET SDK 10.0.403:

- Debug regression checks: passed, 9,621 assertions, including 48 rendering cases.
- Full solution Debug build: passed, 0 warnings and 0 errors.
- Release regression checks: passed, 9,621 assertions.
- Release publication: passed; `Verified localization bundle: nl;tr;de;es;ru`.
  Output is the Windows single-file `WiFiWatch_v1.10.exe`.
- CSharpier 1.3.0 verification: passed. `git diff --check`: passed.
- Analyzer/style verification was attempted for all three application projects
  and failed before analysis because this
  host cannot open the build-host named pipe (`SocketException (13): Permission
  denied`). No analyzer/style success is claimed.

## Windows Checks Not Run Locally

Headless component rendering does not validate WPF/WebView2 interaction, native
tray menus, real toast delivery, visual wrapping, screenshots, installed EXE
startup/extraction, Windows culture discovery or the installed self-updater.
Those require a Windows runtime. This Linux host only cross-builds/publishes the
Windows EXE; it does not execute it.

## Translation Review

All 385 Russian entries remain provisional and need wporgru's review, especially
technical terminology, advice, grammatical case and plural forms. They have not
been supplied or approved by wporgru. Dutch, Turkish, German and Spanish likewise
need native-speaker review. Coverage and runnable checks are complete for the
app-owned text; native-language and interactive Windows validation remain
separate from the checks above.
