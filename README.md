# Wi-Fi Watch

A Windows tray app that tells you when your connection drops, where it went wrong and what to do about it.

![Overview](Screenshots/Overview.png)

| Incidents | History | Settings |
|---|---|---|
| ![Incidents](Screenshots/Incidents.png) | ![History](Screenshots/History.png) | ![Settings](Screenshots/Settings.png) |

## Features
- Live status of your Wi-Fi or cable, router and internet, powerline adapters included
- Every problem logged with where it happened and what to do
- Notifications only for problems that matter
- History, speed tests and a view of every Wi-Fi channel

## Quick start
1. Download the exe from the [latest release](https://github.com/poolfullofmilk/WiFiWatch/releases/latest)
2. Run it, it keeps watching from the tray

## ⚠️ Important
- Needs Windows 11, and location services on for Wi-Fi details
- Windows in another language than English: turn on Read Wi-Fi Natively in Settings
- Windows may warn on the first run because the app is not signed: click More info, then Run anyway

## Technical details
- .NET 10, WPF, Blazor, MudBlazor and SQLite

## Code signing policy
- Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org)
- Committers, reviewers and approvers: [poolfullofmilk](https://github.com/poolfullofmilk)
- Every release is built by [GitHub Actions](.github/workflows/release.yml) from this repository and approved by hand before it is signed
- Privacy: Wi-Fi Watch sends no data about you anywhere. It only pings your router and 1.1.1.1, times a DNS lookup of www.google.com, checks GitHub for a new version once a day and talks to fast.com's servers when a speed test runs. Everything it records stays on your PC
