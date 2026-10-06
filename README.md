# Wifi Watch

Watches Your Wi-Fi Channel, Signal And Ping And Logs Every Change

![Wifi Watch Minutes](Screenshot-Minutes.png)

![Wifi Watch Stats](Screenshot-Stats.png)

## Features
- Live status bar: channel with DFS marker, signal, link rate, router and internet ping
- Alerts when radar pushes your router off a DFS channel, and when it comes back
- Alerts for disconnects, weak signal, ping spikes, packet loss and internet outages
- Traces every outage to show whether it stops at your router or inside the provider network
- Advises the quietest 80 MHz channel block from nearby networks
- Logs every minute, with daily and weekly charts and CSV export
- Lives in the tray and starts with Windows
- Everything stays on your PC

## Quick start
1. Download and run the exe from the [latest release](https://github.com/poolfullofmilk/WifiWatch/releases/latest)
2. Turn on location services if asked
3. Let it run in the tray

## ⚠️ Important
- Windows 11 only shares Wi-Fi details with location services on
- Wi-Fi details are read through `netsh`, which needs Windows in English
- Channel advice follows EU channel rules
- The router settings link opens port 8443, the ASUS default

## Technical details
- C# and WPF on .NET 10, with a Blazor interface through BlazorWebView
- MudBlazor, ApexCharts and SQLite through Entity Framework Core
