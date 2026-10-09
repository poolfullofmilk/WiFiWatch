using System.IO;
using System.Windows;
using ApexCharts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using WiFiWatch.Data;
using WiFiWatch.Services.Integration;
using WiFiWatch.Services.Monitoring;
using WiFiWatch.Services.Storage;

namespace WiFiWatch.Desktop;

public partial class App : Application
{
    private const string ShowSignalName = "WiFiWatch.Show";

    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        Updater.FinishPreviousVersion(eventArgs.Args);

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

        Directory.CreateDirectory(WiFiDbContext.DataDirectory);
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(WiFiDbContext.DataDirectory, "WebView2")
        );

        using (var context = new WiFiDbContext())
        {
            context.Database.Migrate();
        }

        var settings = UserSettings.Load();
        StartupRegistration.Apply(settings.StartWithWindows);

        var monitor = new NetworkMonitor(settings, eventArgs.Args.Contains("--tray"));

        // A Failure In The Page Is Logged, Never Fatal
        DispatcherUnhandledException += (_, exceptionArgs) =>
        {
            exceptionArgs.Handled = true;
            _ = monitor.RecordFailureAsync($"App Failed {exceptionArgs.Exception.GetType().Name}");
        };

        // The Page Resolves The Window Only After It Exists
        MainWindow? window = null;
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddMudServices(configuration =>
        {
            // Snackbars Default To Outlined Icons, Every Other Icon Is Rounded
            configuration.SnackbarConfiguration.SuccessIcon = Icons.Material.Rounded.CheckCircle;
            configuration.SnackbarConfiguration.WarningIcon = Icons.Material.Rounded.Warning;
        });
        services.AddApexCharts();
        services.AddSingleton(monitor);
        services.AddSingleton(_ => window!);

        window = new MainWindow(services.BuildServiceProvider(), monitor);
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
