using System.Globalization;
using System.IO;
using System.Windows;
using ApexCharts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using WiFiWatch.Data;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Localization;
using WiFiWatch.Desktop.Localization;
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

        try
        {
            Start(eventArgs);
        }
        catch (Exception exception)
        {
            // A Failed Start At Sign In Would Otherwise Vanish Without A Word
            MessageBox.Show(
                AppText.Get("StartFailed", AppInfo.DisplayName, exception.Message),
                AppInfo.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(1);
        }
    }

    private void Start(StartupEventArgs eventArgs)
    {
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
        AppText.Initialize(settings.Language, CultureInfo.CurrentUICulture);
        StartupRegistration.Apply(settings.StartWithWindows);

        var monitor = new NetworkMonitor(settings, eventArgs.Args.Contains("--tray"));

        // A Failure In The Page Is Logged, Never Fatal
        DispatcherUnhandledException += (_, exceptionArgs) =>
        {
            exceptionArgs.Handled = true;
            _ = monitor.RecordFailureAsync(
                AppText.Get("App Failed {0}", exceptionArgs.Exception.GetType().Name)
            );
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
            configuration.SnackbarConfiguration.InfoIcon = Icons.Material.Rounded.Info;
            configuration.SnackbarConfiguration.ErrorIcon = Icons.Material.Rounded.Error;

            // Bottom Right, Clear Of The Page Buttons In The App Bar
            configuration.SnackbarConfiguration.PositionClass = Defaults
                .Classes
                .Position
                .BottomRight;
        });
        services.AddLocalization();
        services.AddLocalizationInterceptor<AppLocalizationInterceptor>();
        services.AddApexCharts();
        services.AddSingleton(monitor);
        services.AddSingleton(_ => window!);

        window = new MainWindow(services.BuildServiceProvider(), monitor);
        ThreadPool.RegisterWaitForSingleObject(
            _showSignal!,
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
