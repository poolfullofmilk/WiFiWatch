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
| `Screenshots/` | `Overview.png`, `Incidents.png`, `History.png` and `Settings.png` for the README, taken maximized on a generated sample week (History on 7 Days) with default settings, never on real data |

### WifiWatch.Data

| Path | Holds |
|---|---|
| `WifiDbContext.cs` | `%AppData%\WifiWatch\WifiWatch.db`, `DataDirectory`, the `DbSet`s, enums stored as strings |
| `Models/` | `WifiEvent`, `MinuteSample`, `NeighborSample`, `SpeedTest` (the tables) |
| `ViewModels/` | Records that are never tables: `MonitorStatus`, `WifiReading`, `NeighborReading`, `AdapterInfo`, `EventDetails` (JSON in `WifiEvent.Details`), `TraceHop`, `PeriodSummary`, `WlanNotice`, `ChartPoint`, `HealthBucket`, `HourSummary`, `ChannelBlock`, `SpeedTestProgress` |
| `Enums/` | `EventKind`, `EventSeverity`, `HealthState`, `SpeedTestPhase` |
| `Helpers/` | `AppInfo` (display name, version), `WifiChannels` (DFS and weather radar ranges, timers), `Formatter` (durations, local times, numbers with units, `FormatCount` for 1 Problem or 2 Problems), `EventKindExtensions.ToLabel`, `QueryableExtensions.OrderByColumn`, `HealthBuckets` (worst state per hour or day, self-test), `Problems.IsProblem` |
| `Migrations/` | `Initial`, `AddNeighborSamples`, `AddIncidentsSpeedTestsAndDns` |

### WifiWatch.Services

| Path | Holds |
|---|---|
| `Monitoring/NetworkMonitor.cs` | The one loop, the scopes, every rule. The app's only long-lived service |
| `Monitoring/EventJournal.cs` | Writes events, keeps open incidents by key, decides notifications, the quiet window |
| `Monitoring/ConditionTracker.cs` | Per-minute threshold incidents with persistence and a baseline, self-test |
| `Monitoring/WifiReader.cs` | `netsh wlan show interfaces` and `show networks mode=bssid` parsers, self-test |
| `Monitoring/NativeWifiReader.cs` | The same current connection through `wlanapi` (`WlanQueryInterface`), used when Read Wi-Fi Natively is on |
| `Monitoring/WlanEventReader.cs` | WLAN AutoConfig Operational log, the reason behind disconnects and failed connects |
| `Monitoring/FaultTracer.cs` | MTR-style trace (30 hops, 3 probes) that names where a path stops |
| `Monitoring/DnsProbe.cs` | Raw UDP DNS query timing against the system resolver and 1.1.1.1, self-test |
| `Monitoring/SpeedTester.cs` | Download and upload against fast.com's Netflix servers over parallel streams with live progress, ping under load, bufferbloat grade |
| `Monitoring/AdapterInspector.cs` | Wi-Fi adapter, driver version and date, power saving, roaming |
| `Monitoring/ChannelAdvice.cs` | EU 80 MHz blocks, neighbors per block, the advice line, self-test |
| `Monitoring/SummaryWriter.cs` | The daily summary, `PeriodSummary` for a range, `LoadHoursAsync` (minutes rolled up per hour in SQL) |
| `Monitoring/ConsoleCommand.cs` | Runs `netsh`, `powercfg`, `powershell` and returns stdout; after 30 s it kills the command and throws, so a hung `netsh` fails one tick instead of freezing the loop |
| `Integration/QuickActions.cs` | Every clickable fix: Windows settings pages, Device Manager, the release page, the router admin |
| `Integration/RouterAdmin.cs` | Finds the router's admin page by probing 8443, 443 and 80 on the gateway |
| `Integration/UpdateChecker.cs`, `Updater.cs` | GitHub latest release check, download, swap and clean up |
| `Integration/StartupRegistration.cs` | The `Run` value and the Start menu shortcut, Release only |
| `Storage/UserSettings.cs` | Settings record, JSON in `%AppData%\WifiWatch\settings.json` |
| `Storage/CsvExport.cs` | CSV exports of minutes and events |

### WifiWatch.Desktop

| Path | Holds |
|---|---|
| `App.xaml.cs` | Finishes a previous update, single instance, migrations, settings, DI, starts the monitor |
| `MainWindow.xaml(.cs)` | `BlazorWebView`, tray icon, balloon notifications, minimise to tray, `CloseRequested`, `Exit` |
| `Interop/` | `TrayMenu` (native dark Win32 menu), `WindowCaptionTheme` (dark title bar) |
| `Theming/` | `AppTheme`, `ChartTheme` (LetsWatch, plus palette hex values for charts), `EventKindColors` (colour and icon per kind and severity), `GradeColors` (speed test grade) |
| `Components/Home.razor(.cs)` | Providers, close dialog, app bar with the page buttons, the page switch, update popup |
| `Components/Pages/` | `OverviewPage`, `IncidentsPage`, `HistoryPage`, `SettingsPage` |
| `Components/Shared/Common/` | `ActionChip`, `ChannelStrip`, `ExportMenu`, `Header`, `HealthStrip`, `NowPanel`, `SegmentedButtonGroup`, `SettingSwitch`, `Tooltip` |
| `Components/Shared/Dialogs/` | `IncidentDialog`, `SpeedTestDialog`, `UpdateDialog` |
| `Components/Shared/Tables/` | `DataTable`, `SearchTextField` |
| `wwwroot/` | `index.html`, `CSS/WifiWatch.css`, Nunito with `OFL.txt` |

## Build, Run, Publish

```bash
dotnet build WifiWatch.slnx
```

```bash
dotnet publish WifiWatch.Desktop -c Release
```

Publish writes one file, `WifiWatch.Desktop\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\WifiWatch_v1.5.exe`, about 80 MB. `<Version>` lives in `WifiWatch.Desktop.csproj` only.

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

Everything lives in `%AppData%\WifiWatch`: `WifiWatch.db`, `settings.json` and the `WebView2` profile. Exports go to `Documents\WifiWatch\Exports`. **Nothing is ever deleted**: no retention, no delete buttons, migrations only add. A minute row is about 120 bytes, roughly 60 MB a year.

Times are stored as UTC and shown local. EF reads them back as `Unspecified`, and `ToLocalTime` treats that as UTC, which is what we want.

| Table | One Row Per |
|---|---|
| `Events` | Incident or instant. `OccurredAtUtc`, `EndedAtUtc` (null while open, equal to the start for an instant), `Kind`, `Severity`, `Scope`, `Message`, `Details` JSON, `IsAlert` (kept for old rows) |
| `MinuteSamples` | Minute: link, SSID, band, channel, signal, rates, router and internet ping, jitter, loss, DNS and reference DNS |
| `NeighborSamples` | BSSID per scan every five minutes, `IsOwn`, channel utilization when the access point advertises BSS Load |
| `SpeedTests` | Speed test: download, upload, idle ping, ping under download and upload, grade |

## The Monitor

One `PeriodicTimer` at 1 s in `NetworkMonitor.RunAsync`. Each tick pings the gateway and `1.1.1.1` in parallel. The link and gateway (Ethernet before Wi-Fi) are re-read when `NetworkChange.NetworkAddressChanged` fires and every 5th tick, because listing adapters was three quarters of the app's CPU. Every 5th tick reads Wi-Fi through `netsh` and the WLAN event log, every 15th times DNS, every 300th scans neighbors. At each minute boundary the readings fold into one `MinuteSample` and the per-minute checks run on it. A tick gap over 30 s means the PC slept: every open incident closes at the last tick before it (", Cut Short By Sleep"), the condition tracker starts over, the last Wi-Fi reading is forgotten so the reconnect after waking is not a drop, then `Resumed` is logged and the quiet window restarts. A failure inside a tick logs one `MonitorFailed` per streak and the loop carries on.

### Incidents, Not Loose Rows

`EventJournal` keeps open incidents in a dictionary by key. `OpenAsync` writes the row with no end, `UpdateAsync` changes the message or raises the severity, `CloseAsync` sets the end and a closing message ("Jitter On Wi-Fi For 12m, Peak 48 ms, Average 31 ms"). Instants (`RecordAsync`) end where they start. On start `CloseLeftoversAsync` ends anything still open at the last minute of data with ", Cut Short When Monitoring Stopped". There is no `Recovered` kind any more for new data; old rows keep theirs.

**Notifications.** Critical toasts at once. A warning toasts only if it is still open after 5 minutes (`AlertLongWarnings`), once; at that moment it also raises `EventRecorded` once, because it has just become a problem and Overview and Incidents must list it. Info never toasts, unless forced (`isAlwaysAlerted`: the driver check, and the daily summary when Daily Summary Notification is on and the day had a problem or an outage; a fine day is logged quietly). The tray icon carries a dot, amber or red, while a problem is open, and its hover text names it. Nothing toasts in the 60 s quiet window after start or resume, because sign in and wake flap the link; forced alerts and `LocationBlocked` ignore the window.

**What counts as a problem.** `Problems.IsProblem`: Critical, or a Warning that lasted, or is still open, at least `Problems.WarningMinutes` (5), the same wait a warning toast has. Overview, the Problems view of Incidents, History, the health strip and the summaries all use it, so a warning too short to notify never counts. Old v1.0 rows are Warning instants and so never count; Everything still shows them.

**Per-minute checks** go through `ConditionTracker`. A check opens an incident after 2 bad minutes in a row (or 1 severe minute) and closes after 2 good ones, so a lone bad minute is Wi-Fi being Wi-Fi. A minute with no value (Wi-Fi gone while on a cable, no replies at all) counts as clear, or a Weak Signal incident would stay open all day on Ethernet. For higher-is-worse values the limit is the larger of the setting and a baseline: median plus 3 × 1.4826 × MAD over the last 60 good minutes, used once 10 minutes are in. Severe (Critical) is 3× the setting, or 10% loss.

| Check | Value | Scope |
|---|---|---|
| Weak signal | Signal, Wi-Fi only, never Critical | Wi-Fi |
| Ping spike, packet loss to the router | Router ping, loss | Wi-Fi, or Home Network on Ethernet |
| Ping spike at the provider | Internet ping, only while the router ping is fine | Internet Provider |
| Packet loss at the provider | Internet loss, only while the router loses less than 2% and the internet is not down | Internet Provider |
| Slow DNS | System resolver time, with the 1.1.1.1 time in the context | DNS |

Only three limits are settings: Weak Signal (-70 dBm), Router Ping (20 ms) and Internet Ping (60 ms). Packet loss (2%, two lost pings of 60) and slow DNS (150 ms) are constants in `NetworkMonitor`. Slow Link and Router Jitter checks were removed: link rate is a cause rather than something people feel and misfired on every 2.4 GHz or 40 MHz link, and jitter only rose with router ping and opened a second incident for the same minutes. Old `SlowLink` and `JitterSpike` rows keep their kinds.

Speed test minutes and the minute after are skipped, because a speed test loads the line on purpose. Minutes closing inside the quiet window are skipped too, and DNS is not probed then: the first minute is partial and its first DNS query is cold (hundreds of milliseconds), which used to open a Critical Slow DNS incident on every start.

**Instant incidents** in `NetworkMonitor`: internet down (5 failed internet pings while the router answers, Critical, always traced), Wi-Fi disconnect, wired offline, failed connects, location blocked. Each takes the Windows reason from the WLAN log when one arrives within 2 minutes (event 8003 disconnected, 8002 connect failed; property 6 is the reason).

**Where the problem is.** Every incident carries a scope, and incidents get a trace (`FaultTracer`, at most one per 5 minutes, internet down always). The trace summary says the path is clear, stops at the router (the line to the provider is down), or stops after hop N inside the provider network. Hops go into `EventDetails.Trace`, shown in `IncidentDialog`. The trace is written by row id (`EventJournal.ChangeDetailsAsync`), because a short outage often ends before its trace does. A trace stops after 4 silent hops in a row instead of waiting out all 30.

### DFS

- Channels 52 to 144 are DFS. Leaving one logs `DfsEviction` (Critical, so it toasts), with "Router May Return From HH:mm": the EU non-occupancy period is 30 minutes. Entering one logs `DfsReturn` with the time away.
- Returning to a DFS channel means a 60 s channel check, 10 minutes on the weather radar channels 120 to 128; a weather radar channel is warned about once per run.
- `MonitorStatus.DfsFreeAtUtc` adds "DFS Free At" to the connection line on Overview.
- Channel advice counts distinct neighbors at 30% signal or more per EU 80 MHz block (36 to 48, 52 to 64, 100 to 112, 116 to 128). Two or more evictions in the range while on DFS advises 36 to 48; otherwise the quietest block, ties keep the current block, then prefer no radar.

### Daily Jobs

After 60 ticks on each new day: write yesterday's summary if missing ("yyyy-MM-dd dddd: 99.8% Online, 3 Problems..."), deduplicated by message prefix; the weekly summary is gone, old rows keep their kind; inspect the adapter (an old driver, over 18 months, logs one `DriverCheck` per driver version); check for updates. With Nightly Speed Test on, a speed test runs at 03:00.

## Measurements

- **netsh lines end in `\r\n`**, so a value pattern is `.*` plus `Trim`. Labels are English, so a non-English Windows breaks parsing; Read Wi-Fi Natively avoids it. The native read was checked on this PC: identical values, about 8 ms instead of 65 ms, no process started, and no location in use record. It stays off by default, `netsh` is the proven path. The neighbor scan always uses `netsh`. Link rates can be decimals (`286.8`).
- **Location services must be on.** Windows 11 hides Wi-Fi details from `netsh` without it. `netsh.exe` does the WLAN call, so only Location services and "Let desktop apps access your location" matter.
- **DNS timing** sends a raw UDP A query for `www.google.com`, which every resolver has cached, so it measures the resolver and not the web. A system lookup that times out counts as the full 2 s while 1.1.1.1 still answers, and the minute keeps the median of its 4 lookups, so one lost query is ignored and a resolver that stops answering opens a Critical Slow DNS.
- **Speed test** asks `api.fast.com` for 5 Netflix servers (Netflix picks them, often inside the provider's own network; the signed URLs last an hour, so every test asks again), then runs off the UI thread with 8 parallel streams spread round robin over them for 8 s each way: plain GETs of the 25 MiB test file and 25 MB POSTs to the same URLs, counted per 1 MB as they are written. The live number is the last second; the result is the 90th percentile of those one second readings after the first 2 s. One failed stream ends the test. Pings 1.1.1.1 every 250 ms throughout, about 1 GB per test. The token is fast.com's public app token, unchanged since 2016 and used the same way by Home Assistant; fast.com has no terms of its own. Cloudflare was dropped: since 2026 it caps clients without its own Referer at roughly one test per hour (HTTP 429 for 54 minutes), and faking the Referer gets around an access control. On this PC every server lands between 540 and 640 Mbps down, because the 5 GHz Wi-Fi link (1201 Mbps link rate) is the limit, not the server. `SpeedTestDialog` shows it live through `IProgress<SpeedTestProgress>`. Grade from the added latency under load: under 5 ms A+, 30 A, 60 B, 200 C, 400 D, else F.
- **Adapter**: the first Wireless80211 interface that is not a virtual adapter, preferring one that is up (the Wi-Fi Direct virtual adapters come first otherwise). Driver date comes from the network class registry key as `M-d-yyyy`; power saving from `powercfg` on the current scheme; roaming from `Get-NetAdapterAdvancedProperty` with `-ErrorAction SilentlyContinue`, because PowerShell 5.1 prints errors to stdout here.
- **Router admin** is found by probing `https://gateway:8443`, `https://gateway`, `http://gateway` with a 400 ms timeout, so no router brand is assumed.

## Updates

`UpdateChecker` reads the redirect of `releases/latest` (no API token, no rate limit) and returns that tag, or null when GitHub cannot be reached; `NetworkMonitor.CheckForUpdateAsync` compares it with `AppInfo.Version`, so Settings says Could Not Reach GitHub when offline instead of Is The Latest. `NetworkMonitor.CheckForUpdateAsync` records one `UpdateAvailable` per new version and raises `UpdateFound`.

`Home` shows `UpdateDialog` once per run when an update is known and `ShowUpdatePopup` is on, on first render and on `UpdateFound`. The dialog has a "Don't Show Again" checkbox that turns the setting off; Settings has the same switch, the version, and Check For Updates or Update Now. While downloading, both Update Now buttons stay blue and swap their icon for a spinner beside "Downloading"; a second click is ignored.

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
- **Overview** (is it fine now, and what went wrong lately): `NowPanel` with the verdict (the worst open problem from `EventJournal.OpenProblemsAsync`, else Offline, else All Good), a What To Do line from `QuickActions.AdviceFor`, its fix button, the router button, Speed Test, and live Signal, Wi-Fi Speed, Router and Internet. Values stay plain and turn amber or red only past a setting. Then the last 24 hours as a `HealthStrip`, and the six newest problems as cards that open `IncidentDialog`. It reloads on every recorded event and once a minute, so the strip rolls over each hour. Open incidents show Ongoing rather than a length, because a length would freeze between redraws; the live length is in the verdict.
- **Incidents** (what happened): one `DataTable` of When, What, Where, Length and Details. Problems (by `Problems.IsProblem`) by default, Everything on the segmented toggle. Reloads on every recorded event. A row opens `IncidentDialog`: time range, message, What To Do, where in plain words, Windows reason, context, trace hops, and one fix button from `QuickActions.ForEvent`.
- **History** (how was it over a period): a `MudDateRangePicker` with Today, 7 Days and 30 Days on the left of its action bar (they apply at once) and Cancel and OK on the right, so picking days loads nothing until OK; the minutes CSV export on the right. Four tiles from `SummaryWriter.SummarizeAsync` (Online, Problems, Longest Outage, Fastest Download), the `HealthStrip` per hour up to 3 days and per day beyond, one ping chart (Router grey, Internet blue, DNS dashed violet), the Wi-Fi Channel panel, and the speed tests in range. The channel panel is a `ChannelStrip`: one cell per EU channel grouped by 80 MHz block (36 to 140, or 1 to 13 when on 2.4 GHz), yours blue, a block neighbour at 30% or more amber (it shares your airtime), audible ones elsewhere grey, the network count in each cell and, in the tooltip the channel, Yours, then one network per line with its dBm, numbered once there are several (`.mud-tooltip` keeps line breaks and aligns left), from one DISTINCT query (the strongest reading per network and channel is taken in memory) plus an average for airtime. Radar evictions, airtime busy from your access point's BSS Load, and the advice line sit with it.
- **`HealthStrip`** is one cell per bucket from `HealthBuckets.Build`: red for any Critical problem touching it, amber for a Warning, green when watched and fine, an outlined empty cell when the app was not running. Open problems run until now.
- **Settings** is four sections: General switches, Problem Limits (three fields, each with a helper line), Wi-Fi Adapter chips, About with version and updates.
- **Raw minutes** have no page; the History export holds every column.
- **Dialogs** have no corner X, each has its own Close or Cancel. Tables inside them are bordered and outlined with 10 px corners (`rounded-inner` over `--radius-inner`).
- **Icons** are Rounded and filled (Error and Warning, never the Outline ones). MudBlazor's own defaults are overridden too: the date picker, pager, sort arrow, checkboxes and the snackbar icons in `App.xaml.cs`.
- **A router that ignores ping** (no reply all minute while the internet answers) shows Router - No Answer in grey, skips the router checks and still runs the provider checks.
- **Longest Outage** ignores Info disconnects: Wi-Fi lost while a cable holds is no outage.
- **Loading stays flat as data grows.** Overview and History never load minute rows: `SummaryWriter.LoadHoursAsync` groups them per UTC hour in SQL once per load, and the strip, the ping chart and the summary tiles all fold those hours (weighted by minute count). Measured on a synthetic year: Overview 10 ms, Today 16 ms, 7 Days 110 ms, 30 Days about 0.1 s, a whole year about 1.5 s. Incidents pages on the server. Grouping by UTC hour is exact for whole hour time zones only.
- **Charts** take `@key="_renderKey"`, bumped on every load, because `ApexChart` does not redraw when only its items change. Hex values come from `ChartTheme`, which reads them from the `AppTheme` palette.
- **No Bootstrap**, MudBlazor utilities only (`mb-6` is the 24 px gap). `DataTable` catches the `OperationCanceledException` a superseded `ServerData` call throws and returns the last good page.

## Things That Look Wrong But Are Not

- The internet target is `1.1.1.1`, chosen as a neutral anycast address; v1.0 used another one, so old minutes are not exactly comparable.
- `ProviderPing` is skipped while the router ping is bad, and `ProviderLoss` while the router loses packets or the internet is down: the problem is then local, and counting it twice would blame the provider.
- Old `PingSpike` rows mentioning jitter were moved to `JitterSpike` by the migration, and old rows are instants with a severity from `IsAlert`.
- `ConditionTracker` uses `Math.Max(setting, baseline)`: the baseline only raises the bar on a naturally noisy link, never lowers it below the user's setting. It cannot rescue a limit set below someone's normal, which is why the defaults sit above common connections (Starlink and 4G run 40 to 50 ms to 1.1.1.1).
- A saved `settings.json` keeps its old limits; defaults only fill keys that are missing, and removed keys are ignored.
- Background cost was measured: about 0.5% of one core hidden in the tray, no measurable effect on router latency from the `netsh` reads, and the 5 minute neighbor read only returns Windows' cached list (never `WlanScan`, which takes the radio off channel). The 5 s `netsh wlan show interfaces` spawn is the biggest remaining cost; the native `WlanQueryInterface` would remove it and the English only limit, but may show the location in use icon.
- `EventJournal.HasMessageStartingWithAsync`, `OpenProblemsAsync` and `CloseLeftoversAsync` are static; they only touch the database. Severity is stored as text, so `OpenProblemsAsync` ranks it in memory.

## Verifying

- **Self-tests** run from static constructors with `Debug.Assert`: `WifiReader`, `ChannelAdvice`, `ConditionTracker`, `DnsProbe`, `HealthBuckets`. A failing one kills a Debug launch with `FailFast` and the message `SelfTestPasses()` in the Application event log. To run them all headless, a file-based app with `#:project` pointing at `WifiWatch.Services.csproj` and `#:property TargetFramework=net10.0-windows` can invoke every `SelfTestPasses` through reflection.
- **Analyzers** per project: `dotnet format analyzers <project>.csproj --verify-no-changes --severity info` and the same with `style`, for all three projects.
- **Look at the UI** by launching with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and driving it over the Chrome DevTools Protocol from Node (`/json`, `Page.captureScreenshot`, `Runtime.evaluate`, `Input.dispatchMouseEvent`). The page runs at a device pixel ratio of 1.25, so CSS coordinates are screenshot pixels divided by 1.25.
- **The update popup** can be seen by building with `-p:Version=0.9` and waiting about 70 s for the daily jobs.
- **One instance only.** A second launch, including a Debug build, just shows the first window and exits; stop the installed app before running a build.
