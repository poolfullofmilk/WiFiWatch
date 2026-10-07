# WifiWatch

A Windows 11 tray app that watches the network connection and keeps a local log of it: the Wi-Fi channel (DFS or not), band, signal, link rate, router and internet ping, jitter, packet loss and DNS timing. Problems become incidents with a start, an end, a place (Wi-Fi, home network, internet provider, DNS) and a reason, and the serious ones show as Windows notifications. It is a general tool for any home network and any provider; nothing in it may be specific to one router brand or one ISP.

.NET 10, WPF hosting Blazor in a `BlazorWebView`, MudBlazor, Blazor-ApexCharts, EF Core on SQLite. The shown name is "Wifi Watch"; the project, namespaces, data folder and exe are `WifiWatch`. Repository: `github.com/poolfullofmilk/WifiWatch`, GPL-3.0.

The global `~/.claude/CLAUDE.md` holds every shared convention (git, releases, comments, code style, design system, checks). This file only holds what is specific to WifiWatch.

## Layout

| Path | Holds |
|---|---|
| `WifiWatch.slnx` | The three projects |
| `WifiWatch.Data` | `net10.0`, no Windows dependencies. EF context, models, migrations, view models, enums, small helpers |
| `WifiWatch.Services` | `net10.0-windows`. Everything that watches, measures, stores or talks to Windows |
| `WifiWatch.Desktop` | WPF exe, `Microsoft.NET.Sdk.Razor`, `net10.0-windows10.0.17763.0`. Window, tray, Blazor UI, theming |
| `Screenshots/` | `Overview.png`, `Incidents.png`, `History.png` and `Settings.png` for the README |

### WifiWatch.Data

| Path | Holds |
|---|---|
| `WifiDbContext.cs` | `%AppData%\WifiWatch\WifiWatch.db`, `DataDirectory`, the `DbSet`s, enums stored as strings |
| `Models/` | `WifiEvent`, `MinuteSample`, `NeighborSample`, `SpeedTest` (the tables) |
| `ViewModels/` | Records that are never tables: `MonitorStatus`, `WifiReading`, `NeighborReading`, `AdapterInfo`, `EventDetails` (JSON in `WifiEvent.Details`), `TraceHop`, `PeriodSummary`, `WlanNotice`, `ChartPoint`, `HealthBucket`, `ChannelBlock` |
| `Enums/` | `EventKind`, `EventSeverity`, `HealthState` |
| `Helpers/` | `AppInfo` (display name, version), `WifiChannels` (DFS and weather radar ranges, timers), `Formatter` (durations, local times, numbers with units), `EventKindExtensions.ToLabel`, `QueryableExtensions.OrderByColumn`, `HealthBuckets` (worst state per hour or day, self-test), `Problems.IsProblem` |
| `Migrations/` | `Initial`, `AddNeighborSamples`, `AddIncidentsSpeedTestsAndDns` |

### WifiWatch.Services

| Path | Holds |
|---|---|
| `Monitoring/NetworkMonitor.cs` | The one loop, the scopes, every rule. The app's only long-lived service |
| `Monitoring/EventJournal.cs` | Writes events, keeps open incidents by key, decides notifications, the quiet window |
| `Monitoring/ConditionTracker.cs` | Per-minute threshold incidents with persistence and a baseline, self-test |
| `Monitoring/WifiReader.cs` | `netsh wlan show interfaces` and `show networks mode=bssid` parsers, self-test |
| `Monitoring/WlanEventReader.cs` | WLAN AutoConfig Operational log, the reason behind disconnects and failed connects |
| `Monitoring/FaultTracer.cs` | MTR-style trace (30 hops, 3 probes) that names where a path stops |
| `Monitoring/DnsProbe.cs` | Raw UDP DNS query timing against the system resolver and 1.1.1.1, self-test |
| `Monitoring/SpeedTester.cs` | Cloudflare download and upload with ping under load, bufferbloat grade |
| `Monitoring/AdapterInspector.cs` | Wi-Fi adapter, driver version and date, power saving, roaming |
| `Monitoring/ChannelAdvice.cs` | EU 80 MHz blocks, neighbors per block, the advice line, self-test |
| `Monitoring/SummaryWriter.cs` | Daily and weekly summaries, `PeriodSummary` for a range |
| `Monitoring/ConsoleCommand.cs` | Runs `netsh`, `powercfg`, `powershell` and returns stdout |
| `Integration/QuickActions.cs` | Every clickable fix: Windows settings pages, Device Manager, the release page, the router admin |
| `Integration/RouterAdmin.cs` | Finds the router's admin page by probing 8443, 443 and 80 on the gateway |
| `Integration/UpdateChecker.cs`, `Updater.cs` | GitHub latest release check, download, swap and clean up |
| `Integration/StartupRegistration.cs` | The `Run` value and the Start menu shortcut, Release only |
| `Storage/UserSettings.cs` | Settings record, JSON in `%AppData%\WifiWatch\settings.json` |
| `Storage/CsvExport.cs`, `ReportWriter.cs` | CSV exports and the HTML connection report |

### WifiWatch.Desktop

| Path | Holds |
|---|---|
| `App.xaml.cs` | Finishes a previous update, single instance, migrations, settings, DI, starts the monitor |
| `MainWindow.xaml(.cs)` | `BlazorWebView`, tray icon, balloon notifications, minimise to tray, `CloseRequested`, `Exit` |
| `Interop/` | `TrayMenu` (native dark Win32 menu), `WindowCaptionTheme` (dark title bar) |
| `Theming/` | `AppTheme`, `ChartTheme` (LetsWatch, plus palette hex values for charts), `EventKindColors` (colour and icon per kind and severity) |
| `Components/Home.razor(.cs)` | Providers, close dialog, app bar with the page buttons, the page switch, update popup |
| `Components/Pages/` | `OverviewPage`, `IncidentsPage`, `HistoryPage`, `SettingsPage` |
| `Components/Shared/Common/` | `ActionChip`, `ExportMenu`, `Header`, `HealthStrip`, `NowPanel`, `SegmentedButtonGroup`, `SettingSwitch`, `Tooltip` |
| `Components/Shared/Dialogs/` | `IncidentDialog`, `UpdateDialog` |
| `Components/Shared/Tables/` | `DataTable`, `SearchTextField` |
| `wwwroot/` | `index.html`, `CSS/WifiWatch.css`, Nunito with `OFL.txt` |

## Build, Run, Publish

```bash
dotnet build WifiWatch.slnx
```

```bash
dotnet publish WifiWatch.Desktop -c Release
```

Publish writes one file, `WifiWatch.Desktop\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\WifiWatch_v1.2.exe`, about 80 MB. `<Version>` lives in `WifiWatch.Desktop.csproj` only.

Installing is copying that exe to `%LocalAppData%\Programs\WifiWatch` and running it once. Every Release launch rewrites the `Run` value (with `--tray`) and `Start Menu\Programs\01 Apps\Wifi Watch.lnk` to point at itself. A release on GitHub carries exactly one asset named `WifiWatch_v<version>.exe` under the tag `v<version>`, because the updater builds that URL.

- **The Desktop TFM is `net10.0-windows10.0.17763.0`.** BlazorWebView 10 uses `WebView2CompositionControl`, which needs `Microsoft.Windows.SDK.NET`; plain `net10.0-windows` throws `FileNotFoundException` on `Show`.
- **`BundleWebRoot` and `RemoveLooseWebRoot` make the exe truly single.** Static web assets are copied after the bundle is computed, so `BundleWebRoot` runs `CopyStaticWebAssetsToPublishDirectory` first and adds `wwwroot` to `ResolvedFileToPublish`.
- **`UseWindowsForms` is only for `NotifyIcon`.** The csproj removes the `System.Drawing` and `System.Windows.Forms` global usings; code reaches them through `Drawing` and `Forms` aliases.
- **Migrations** live in Data but need the Desktop project as the startup project, which carries the Debug-only `Microsoft.EntityFrameworkCore.Design`:

```bash
dotnet ef migrations add Name --project WifiWatch.Data --startup-project WifiWatch.Desktop --output-dir Migrations
```

  Then convert the new migration to a file-scoped namespace, run CSharpier with `--include-generated`, and check `dotnet ef migrations has-pending-model-changes`. A migration that changes the meaning of old rows backfills them with `migrationBuilder.Sql` (see `AddIncidentsSpeedTestsAndDns`).

## Data

Everything lives in `%AppData%\WifiWatch`: `WifiWatch.db`, `settings.json` and the `WebView2` profile. Exports go to `Documents\WifiWatch\Exports`, reports to `Documents\WifiWatch\Reports`. **Nothing is ever deleted**: no retention, no delete buttons, migrations only add. A minute row is about 120 bytes, roughly 60 MB a year.

Times are stored as UTC and shown local. EF reads them back as `Unspecified`, and `ToLocalTime` treats that as UTC, which is what we want.

| Table | One Row Per |
|---|---|
| `Events` | Incident or instant. `OccurredAtUtc`, `EndedAtUtc` (null while open, equal to the start for an instant), `Kind`, `Severity`, `Scope`, `Message`, `Details` JSON, `IsAlert` (kept for old rows) |
| `MinuteSamples` | Minute: link, SSID, band, channel, signal, rates, router and internet ping, jitter, loss, DNS and reference DNS |
| `NeighborSamples` | BSSID per scan every five minutes, `IsOwn`, channel utilization when the access point advertises BSS Load |
| `SpeedTests` | Speed test: download, upload, idle ping, ping under download and upload, grade |

## The Monitor

One `PeriodicTimer` at 1 s in `NetworkMonitor.RunAsync`. Each tick detects the link and gateway (Ethernet before Wi-Fi) and pings the gateway and `1.1.1.1` in parallel. Every 5th tick reads Wi-Fi through `netsh` and the WLAN event log, every 15th times DNS, every 300th scans neighbors. At each minute boundary the readings fold into one `MinuteSample` and the per-minute checks run on it. A tick gap over 30 s logs `Resumed` and restarts the quiet window. A failure inside a tick logs one `MonitorFailed` per streak and the loop carries on.

### Incidents, Not Loose Rows

`EventJournal` keeps open incidents in a dictionary by key. `OpenAsync` writes the row with no end, `UpdateAsync` changes the message or raises the severity, `CloseAsync` sets the end and a closing message ("Jitter On Wi-Fi For 12m, Peak 48 ms, Average 31 ms"). Instants (`RecordAsync`) end where they start. On start `CloseLeftoversAsync` ends anything still open at the last minute of data with ", Cut Short When Monitoring Stopped". There is no `Recovered` kind any more for new data; old rows keep theirs.

**Notifications.** Critical toasts at once. A warning toasts only if it is still open after 5 minutes (`AlertLongWarnings`), once. Info never toasts, unless forced (`isAlwaysAlerted`: the driver check, and the summaries when Daily Summary Notification is on). Nothing toasts in the 60 s quiet window after start or resume, because sign in and wake flap the link; forced alerts and `LocationBlocked` ignore the window.

**What counts as a problem.** `Problems.IsProblem`: Critical, or a Warning that lasted, or is still open, at least `Problems.WarningMinutes` (5), the same wait a warning toast has. Overview, the Problems view of Incidents, History, the health strip, the summaries and the report all use it, so a warning too short to notify never counts. Old v1.0 rows are Warning instants and so never count; Everything still shows them.

**Per-minute checks** go through `ConditionTracker`. A check opens an incident after 2 bad minutes in a row (or 1 severe minute) and closes after 2 good ones, so a lone bad minute is Wi-Fi being Wi-Fi. For higher-is-worse values the limit is the larger of the setting and a baseline: median plus 3 × 1.4826 × MAD over the last 60 good minutes, used once 10 minutes are in. Severe (Critical) is 3× the setting, or 10% loss.

| Check | Value | Scope |
|---|---|---|
| Weak signal, slow link | Signal and receive rate, Wi-Fi only | Wi-Fi |
| Ping spike, jitter, packet loss to the router | Router ping, jitter, loss | Wi-Fi, or Home Network on Ethernet |
| Ping spike at the provider | Internet ping, only while the router ping is fine | Internet Provider |
| Packet loss at the provider | Internet loss, only while the router loses less than the setting and the internet is not down | Internet Provider |
| Slow DNS | System resolver time, with the 1.1.1.1 time in the context | DNS |

Speed test minutes and the minute after are skipped, because a speed test loads the line on purpose. Minutes closing inside the quiet window are skipped too, and DNS is not probed then: the first minute is partial and its first DNS query is cold (hundreds of milliseconds), which used to open a Critical Slow DNS incident on every start.

**Instant incidents** in `NetworkMonitor`: internet down (5 failed internet pings while the router answers, Critical, always traced), Wi-Fi disconnect, wired offline, failed connects, location blocked. Each takes the Windows reason from the WLAN log when one arrives within 2 minutes (event 8003 disconnected, 8002 connect failed; property 6 is the reason).

**Where the problem is.** Every incident carries a scope, and incidents get a trace (`FaultTracer`, at most one per 5 minutes, internet down always). The trace summary says the path is clear, stops at the router (the line to the provider is down), or stops after hop N inside the provider network. Hops go into `EventDetails.Trace`, shown in `IncidentDialog` and the report.

### DFS

- Channels 52 to 144 are DFS. Leaving one logs `DfsEviction` (Critical, so it toasts), with "Router May Return From HH:mm": the EU non-occupancy period is 30 minutes. Entering one logs `DfsReturn` with the time away.
- Returning to a DFS channel means a 60 s channel check, 10 minutes on the weather radar channels 120 to 128; a weather radar channel is warned about once per run.
- `MonitorStatus.DfsFreeAtUtc` adds "DFS Free At" to the connection line on Overview.
- Channel advice counts distinct neighbors at 30% signal or more per EU 80 MHz block (36 to 48, 52 to 64, 100 to 112, 116 to 128). Two or more evictions in the range while on DFS advises 36 to 48; otherwise the quietest block, ties keep the current block, then prefer no radar.

### Daily Jobs

After 60 ticks on each new day: write any missing daily summary (yesterday, "yyyy-MM-dd dddd: 99.8% Online, 3 Incidents...") and weekly summary ("Week Of yyyy-MM-dd"), deduplicated by message prefix; inspect the adapter (an old driver, over 18 months, logs one `DriverCheck` per driver version); check for updates. With Nightly Speed Test on, a speed test runs at 03:00.

## Measurements

- **netsh lines end in `\r\n`**, so a value pattern is `.*` plus `Trim`. Labels are English, so a non-English Windows breaks parsing; the fix would be the Native Wifi API. Link rates can be decimals (`286.8`).
- **Location services must be on.** Windows 11 hides Wi-Fi details from `netsh` without it. `netsh.exe` does the WLAN call, so only Location services and "Let desktop apps access your location" matter.
- **DNS timing** sends a raw UDP A query for `www.google.com`, which every resolver has cached, so it measures the resolver and not the web.
- **Speed test** downloads from `speed.cloudflare.com/__down` and uploads to `/__up` for 8 s each, pinging 1.1.1.1 every 250 ms. Grade from the added latency under load: under 5 ms A+, 30 A, 60 B, 200 C, 400 D, else F.
- **Adapter**: the first Wireless80211 interface that is not a virtual adapter, preferring one that is up (the Wi-Fi Direct virtual adapters come first otherwise). Driver date comes from the network class registry key as `M-d-yyyy`; power saving from `powercfg` on the current scheme; roaming from `Get-NetAdapterAdvancedProperty` with `-ErrorAction SilentlyContinue`, because PowerShell 5.1 prints errors to stdout here.
- **Router admin** is found by probing `https://gateway:8443`, `https://gateway`, `http://gateway` with a 400 ms timeout, so no router brand is assumed.

## Updates

`UpdateChecker` reads the redirect of `releases/latest` (no API token, no rate limit) and compares the tag with `AppInfo.Version`. `NetworkMonitor.CheckForUpdateAsync` records one `UpdateAvailable` per new version and raises `UpdateFound`.

`Home` shows `UpdateDialog` once per run when an update is known and `ShowUpdatePopup` is on, on first render and on `UpdateFound`. The dialog has a "Don't Show Again" checkbox that turns the setting off; Settings has the same switch, the version, and Check For Updates or Update Now.

Update Now: `Updater.DownloadAsync` saves `WifiWatch_v<new>.exe` beside the running exe (through a `.download` file), `Updater.Launch` starts it with `--after-update <pid>`, the window exits. The new exe calls `Updater.FinishPreviousVersion` first in `OnStartup`: it waits up to 15 s for the old process and deletes the other `WifiWatch_v*.exe` files beside it (replaced builds, not data). A Debug build or a renamed exe (`Updater.CanInstall` false) opens the release page instead.

## Window Behaviour

- **It never closes by itself.** Minimise hides to the tray. Close raises `MainWindow.CloseRequested`; `Home` answers with a `MudMessageBox`: Keep Running (blue, hides) or Exit. Dismissing it changes nothing. Only Exit in the dialog or tray ends the app.
- **`SessionEnding` sets the exit flag** so Windows shutdown is never held up. `DispatcherUnhandledException` is handled and recorded through `NetworkMonitor.RecordFailureAsync`.
- **`Home` gets the window from DI** through a factory that only runs after the window exists.
- **The tray menu is a native Win32 popup** (`TrayMenu`), dark through the uxtheme ordinals 135 and 136. The owner window must be foreground before `TrackPopupMenuEx` and get a `WM_NULL` after, or the menu never closes.
- **`Started` explains the gap**: "Started At Sign In, Off Since 2026-10-07 01:12 (7h 3m), PC Booted 2026-10-07 08:10". `--tray` means started at sign in.

## UI

The look follows LetsWatch: navigation in the app bar, section titles above panels, cards that darken on hover. Every page answers one question.

- **App bar**: the name, then four labelled page buttons (Overview, Incidents, History, Settings), the open one filled blue. A dot on Overview, amber or red, while a problem is still open. `Home` swaps pages with a plain `switch`, so every page loads fresh when opened and owns its own event subscriptions.
- **Overview** (is it fine now, and what went wrong lately): `NowPanel` with the verdict (the worst open problem from `EventJournal.OpenProblemsAsync`, else Offline, else All Good), its fix button, the router button, Speed Test, and live Signal, Wi-Fi Speed, Router and Internet. Values stay plain and turn amber or red only past a setting. Then the last 24 hours as a `HealthStrip`, and the six newest problems as cards that open `IncidentDialog`.
- **Incidents** (what happened): one `DataTable` of When, What, Where, Length and Details. Problems (by `Problems.IsProblem`) by default, Everything on the segmented toggle. Reloads on every recorded event. A row opens `IncidentDialog`: time range, message, where in plain words, Windows reason, context, trace hops, and one fix button from `QuickActions.ForEvent`.
- **History** (how was it over a period): Today, 7 Days and 30 Days as a `SegmentedButtonGroup` beside a `MudDateRangePicker` for any range; the minutes CSV export and Report on the right. Four tiles from `SummaryWriter.SummarizeAsync` (Online, Problems, Longest Outage, Fastest Download), the `HealthStrip` per hour up to 3 days and per day beyond, one ping chart (Router grey, Internet blue, DNS dashed), the speed tests in range, and the channel advice with its radar eviction count.
- **`HealthStrip`** is one cell per bucket from `HealthBuckets.Build`: red for any Critical problem touching it, amber for a Warning, green when watched and fine, an outlined empty cell when the app was not running. Open problems run until now.
- **Settings** is four sections: General switches, Problem Limits, Wi-Fi Adapter chips, About with version and updates.
- **Raw minutes** have no page; the History export holds every column.
- **Charts** take `@key="_renderKey"`, bumped on every load, because `ApexChart` does not redraw when only its items change. Hex values come from `ChartTheme`, which reads them from the `AppTheme` palette.
- **No Bootstrap**, MudBlazor utilities only (`mb-6` is the 24 px gap). `DataTable` catches the `OperationCanceledException` a superseded `ServerData` call throws and returns the last good page.

## Things That Look Wrong But Are Not

- The internet target is `1.1.1.1`, chosen as a neutral anycast address; v1.0 used another one, so old minutes are not exactly comparable.
- `ProviderPing` is skipped while the router ping is bad, and `ProviderLoss` while the router loses packets or the internet is down: the problem is then local, and counting it twice would blame the provider.
- Old `PingSpike` rows mentioning jitter were moved to `JitterSpike` by the migration, and old rows are instants with a severity from `IsAlert`.
- `ConditionTracker` uses `Math.Max(setting, baseline)`: the baseline only raises the bar on a naturally noisy link, never lowers it below the user's setting.
- `EventJournal.HasMessageStartingWithAsync`, `OpenProblemsAsync` and `CloseLeftoversAsync` are static; they only touch the database. Severity is stored as text, so `OpenProblemsAsync` ranks it in memory.

## Verifying

- **Self-tests** run from static constructors with `Debug.Assert`: `WifiReader`, `ChannelAdvice`, `ConditionTracker`, `DnsProbe`, `HealthBuckets`. A failing one kills a Debug launch with `FailFast` and the message `SelfTestPasses()` in the Application event log. To run them all headless, a file-based app with `#:project` pointing at `WifiWatch.Services.csproj` and `#:property TargetFramework=net10.0-windows` can invoke every `SelfTestPasses` through reflection.
- **Analyzers** per project: `dotnet format analyzers <project>.csproj --verify-no-changes --severity info` and the same with `style`, for all three projects.
- **Look at the UI** by launching with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and driving it over the Chrome DevTools Protocol from Node (`/json`, `Page.captureScreenshot`, `Runtime.evaluate`, `Input.dispatchMouseEvent`). The page runs at a device pixel ratio of 1.25, so CSS coordinates are screenshot pixels divided by 1.25.
- **The update popup** can be seen by building with `-p:Version=0.9` and waiting about 70 s for the daily jobs.
- **One instance only.** A second launch, including a Debug build, just shows the first window and exits; stop the installed app before running a build.
