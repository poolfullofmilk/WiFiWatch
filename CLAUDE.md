# WifiWatch

A .NET 10 WPF tray app with a Blazor UI inside a `BlazorWebView`. It watches the Wi-Fi channel (DFS or not), band, signal, link rate, router and internet ping, and logs every change to SQLite. Alerts show as Windows notifications. The global `~/.claude/CLAUDE.md` holds every shared convention and the design system; this file only holds what is specific to WifiWatch.

## Layout

| Path | Holds |
|---|---|
| `App.xaml.cs` | Single instance, migrations, settings, services, starts the monitor |
| `MainWindow.xaml(.cs)` | The `BlazorWebView`, the tray icon, minimise to tray, close through `CloseRequested` |
| `Services/TrayMenu.cs` | The native dark Win32 tray menu |
| `Services/NetworkMonitor.cs` | The one loop: link, pings, Wi-Fi reads, minute summaries, alert rules |
| `Services/WifiReader.cs` | `netsh wlan show interfaces` parser, DFS check, self-test |
| `Services/QuickActions.cs` | Every clickable fix: Windows settings pages and the router admin |
| `Services/CsvExport.cs` | Export to `Documents\WifiWatch\Exports`, never overwrites |
| `Services/UserSettings.cs`, `StartupRegistration.cs`, `WindowCaptionTheme.cs` | Settings JSON, the `Run` key and the Start menu shortcut, the dark title bar |
| `Data/` | `WifiDbContext`, `WifiEvent`, `MinuteSample`, `EventKind`, migrations |
| `Components/Home.razor(.cs)` | The only page: Events, Minutes, Stats and Settings tabs |
| `Components/Shared/` | `DataTable`, `SearchTextField`, `FilterMenu`, `Tooltip`, `SettingSwitch`, `SegmentedButtonGroup` (from LetsWatch), `StatusBar`, `ActionChip`, `ExportMenu` |
| `Theming/`, `wwwroot/CSS/WifiWatch.css` | LetsWatch's `AppTheme`, `ChartTheme` and the CSS tokens it uses, `EventKindColors` (the one colour per event kind) |

## Build, Run, Publish

```bash
dotnet build
```

```bash
dotnet publish -c Release
```

Publish writes one file, `bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\WifiWatch_v1.0.exe`, about 83 MB. Bump `<Version>` for every shipped change; the exe name follows it.

Installing is copying that exe to `%LocalAppData%\Programs\WifiWatch` and running it once. Every Release launch rewrites the `Run` value and `Start Menu\Programs\01 Apps\Wifi Watch.lnk` (the Desktop `Apps` folder is a link to that folder) to point at itself, so a new version only needs the same two steps. Stop the running one first; a second launch only shows the first window.

- **The TFM is `net10.0-windows10.0.17763.0`, not `net10.0-windows`.** BlazorWebView 10 uses `WebView2CompositionControl`, which needs `Microsoft.Windows.SDK.NET`; without the versioned TFM the window throws `FileNotFoundException` on `Show`.
- **`BundleWebRoot` and `RemoveLooseWebRoot` in the csproj make the exe truly single.** Static web assets are copied by their own targets after the bundle is computed, so they never reach it on their own. `BundleWebRoot` runs `CopyStaticWebAssetsToPublishDirectory` first and adds `wwwroot` to `ResolvedFileToPublish`; `IncludeAllContentForSelfExtract` extracts it next to the app at start. Test a publish by copying the exe alone to an empty folder.
- **`Microsoft.EntityFrameworkCore.Design` is Debug only**, so Roslyn stays out of the exe. Add migrations from a Debug build: `dotnet ef migrations add Name --output-dir Data/Migrations`, then run CSharpier.
- **`UseWindowsForms` is only for `NotifyIcon`.** Its balloon tips become Windows 11 notifications. The csproj removes the `System.Drawing` and `System.Windows.Forms` global usings so `Color` and `Application` stay unambiguous; code reaches them through `Drawing` and `Forms` aliases.
- **`StartupRegistration` is a no-op in Debug**, so running from `bin\Debug` never registers itself. A Release exe rewrites the `Run` value and the shortcut on every launch, because the exe name carries the version. The shortcut goes through `WScript.Shell` over `dynamic`, which needs no package.

## Data

Everything lives in `%AppData%\WifiWatch`: `WifiWatch.db`, `settings.json` and the `WebView2` profile (set through `WEBVIEW2_USER_DATA_FOLDER` so no folder appears next to the exe). **Nothing is ever deleted**: no retention, no delete buttons, migrations only add. A minute row is about 100 bytes, roughly 50 MB a year.

Times are stored as UTC and shown local. EF reads them back as `Unspecified`, and `ToLocalTime` treats that as UTC, which is what we want.

## The Monitor

One `PeriodicTimer` at 1 s in `NetworkMonitor.RunAsync`. Each tick: detect the link and the gateway (`NetworkInterface`, Ethernet before Wi-Fi), ping the gateway and `76.76.2.2` in parallel, and every 5th tick read Wi-Fi through `netsh`. At each minute boundary the readings fold into one `MinuteSample` and the threshold rules run on that minute.

| Rule | Fires |
|---|---|
| Channel change | Instantly, compared with the last connected reading so a drop and reconnect on a new channel still counts. `DfsEviction` when leaving 52–144, `DfsReturn` when entering |
| Band change | Instantly, alerts only when leaving 5 GHz |
| Disconnect | Instantly, alerts only when not on Ethernet |
| WAN down | Five failed internet pings while the router answers |
| Weak signal, slow link, router ping or jitter, internet ping, packet loss | Per minute against `UserSettings`, alert once on entry, log a `Recovered` event once on exit |
| Location access | Instantly, Windows 11 hides Wi-Fi details from `netsh` without it |

Alerts wait out a 60 s quiet window after start and after a resume (a tick gap over 30 s), because sign in and wake flap the link. Location still alerts at once. A failure inside a tick logs one `MonitorFailed` per streak and the loop carries on.

- **`netsh` output is parsed by its English labels.** A Windows display language change breaks it; the fix is the Native Wifi API (`WlanQueryInterface`). Link rates can be decimals (`286.8`).
- **No per-app location consent exists for this app.** `netsh.exe` does the WLAN call, so only Location services and "Let desktop apps access your location" matter. The app notices within 5 s.
- **The router admin is `https://{gateway}:8443`**, the ASUS port. Another router needs its own port in `QuickActions.RouterSettings`.

## Window Behaviour

- **It never closes by itself.** Minimise hides straight to the tray. Close (X or Alt+F4) cancels and raises `MainWindow.CloseRequested`; `Home` answers with a `MudMessageBox`: Keep Running (blue, hides) or Exit (default). Dismissing it changes nothing. Before the page has loaded, close just hides. Only the tray's Exit and the dialog's Exit end the app.
- **Windows signing out is never held up**: `SessionEnding` sets the exit flag so the cancel in `OnClosing` stands aside.
- **A failure in the page is logged, never fatal**: `DispatcherUnhandledException` is handled and recorded as `MonitorFailed`.
- **`Home` gets the window from DI.** `App` registers a factory returning the window, which only runs after the window exists.
- **The tray menu is a native Win32 popup** (`TrayMenu`), dark through the undocumented uxtheme ordinals 135 and 136, so it looks like Windows 11's own menus. WinForms' `ContextMenuStrip` draws an Office 2003 style menu, and WPF-UI.Tray has no balloon notifications. The owner window must be foreground before `TrackPopupMenuEx` and get a `WM_NULL` after, or the menu never closes.
- To see the tray menu without clicking the tray, a file-based app with `#:property UseWindowsForms=true` can call `TrayMenu.Show(new Form().Handle, ...)` and a DPI-aware `CopyFromScreen` captures it before an `{ESC}`.

## UI Notes

- **The shown name is "Wifi Watch"**, from `App.DisplayName`: the app bar, the window title, the tray and the shortcut. The csproj sets the same as `AssemblyTitle` and `Product`, so notifications and Task Manager show it too. The project, namespaces, data folder and exe stay `WifiWatch`. The version shows at the bottom of Settings, never in log messages.
- **Glass goes wherever something sits over content**: the app bar, every popover, the dialog scrim and the sticky table headers.
- **Stats has one centred Daily and Weekly toggle** (`SegmentedButtonGroup`) over both charts: ping per hour for 24 hours or 7 days, DFS evictions per hour or per day. Both charts take `@key="_statsRenderKey"`, bumped on every load, because an `ApexChart` does not redraw when only its items change.
- **No Bootstrap.** LetsWatch loads it from a CDN, which an app watching for outages cannot rely on. Only MudBlazor's own utility classes are available, and its spacing scale steps by 4 px: `mb-6` is the 24 px block gap, `pa-4` the 16 px panel padding.
- **The tab bar keeps `AlwaysShowScrollButtons`.** The arrows cap both rounded ends; without them the first tab's square hover pokes out of the curve.
- **Colour means state.** Event kinds take their colour from `EventKindColors` (red broken, amber degraded, green recovered or back, blue informational, grey neutral), as the same `MudChip` in the table and in the filter menu. Status chips are green when healthy against the thresholds in `UserSettings`, amber over a threshold, red when lost or offline; the channel is blue on DFS.
- **Every chip with a fix is an `ActionChip`**: underlined, a tooltip naming the action, and a click opening the page from `QuickActions`. A chip with nothing to fix stays plain.
- **The status bar is its own component** so the 1 s refresh only re-renders the chips, not the tables.
- **The Events table reloads on every recorded event** while its tab is open; when hidden only the badge count updates, because a hidden tab panel's table is disposed.

## Verifying

- `WifiReader`'s static constructor runs `Debug.Assert(SelfTestPasses())`: the parser, decimals, location blocking and DFS classification. Extend it when either changes. To run it headless, a file-based app with `#:project` pointing at `WifiWatch.csproj` and `#:property TargetFramework=net10.0-windows10.0.17763.0` can invoke it through reflection.
- To see the UI, launch with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and drive it over the Chrome DevTools Protocol from Node (`/json` lists the page, `Page.captureScreenshot` and `Runtime.evaluate` do the rest). `PrintWindow` captures WebView2 black.
- A running exe locks `bin`; stop it before building. Only one instance runs; a second launch just shows the first window.
- Every launch logs a `Started` event, which is how restarts and reboots show in the log.
