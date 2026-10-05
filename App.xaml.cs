using System.IO;
using System.Windows;
using ApexCharts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using WifiWatch.Data;
using WifiWatch.Services;

namespace WifiWatch;

public partial class App : Application
{
    private const string ShowSignalName = "WifiWatch.Show";

    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        // A Second Launch Only Shows The First Window
        _showSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowSignalName,
            out var isFirstInstance
        );
        if (!isFirstInstance)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        Directory.CreateDirectory(WifiDbContext.DataDirectory);
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(WifiDbContext.DataDirectory, "WebView2")
        );

        using (var context = new WifiDbContext())
        {
            context.Database.Migrate();
        }

        var settings = UserSettings.Load();
        StartupRegistration.Apply(settings.StartWithWindows);

        var monitor = new NetworkMonitor(settings);
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddMudServices();
        services.AddApexCharts();
        services.AddSingleton(monitor);

        var window = new MainWindow(services.BuildServiceProvider(), monitor);
        ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.InvokeAsync(window.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false
        );

        // Windows Starts Us Hidden In The Tray
        if (!eventArgs.Args.Contains("--tray"))
        {
            window.Show();
        }

        monitor.Start();
    }
}
