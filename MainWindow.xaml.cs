using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using WifiWatch.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WifiWatch;

public partial class MainWindow : Window
{
    private const int BalloonMilliseconds = 5000;
    private const string OpenItem = "Open";
    private const string ExitItem = "Exit";

    private readonly Forms.NotifyIcon _trayIcon;
    private bool _isExiting;

    public MainWindow(IServiceProvider services, NetworkMonitor monitor)
    {
        InitializeComponent();
        WebView.Services = services;

        // Page Background Before The First Paint, No White Flash
        WebView.BlazorWebViewInitialized += (_, eventArgs) =>
            eventArgs.WebView.DefaultBackgroundColor = Drawing.Color.FromArgb(0x20, 0x20, 0x20);
        SourceInitialized += (_, _) => WindowCaptionTheme.Apply(this);
        StateChanged += (_, _) => HideWhenMinimized();

        // Windows Shutting Down Must Never Be Held Up
        Application.Current.SessionEnding += (_, _) => _isExiting = true;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = App.DisplayName,
            Visible = true,
        };
        _trayIcon.MouseUp += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
            else if (eventArgs.Button == Forms.MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        };
        _trayIcon.BalloonTipClicked += (_, _) => ShowFromTray();

        monitor.Alerted += (title, message) =>
            Dispatcher.InvokeAsync(() =>
                _trayIcon.ShowBalloonTip(
                    BalloonMilliseconds,
                    title,
                    message,
                    Forms.ToolTipIcon.Warning
                )
            );
        monitor.StatusChanged += () =>
            Dispatcher.InvokeAsync(() => _trayIcon.Text = monitor.Status.TrayText);
    }

    public event Func<Task>? CloseRequested;

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void Exit()
    {
        _isExiting = true;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void ShowTrayMenu()
    {
        var picked = TrayMenu.Show(
            new WindowInteropHelper(this).EnsureHandle(),
            OpenItem,
            null,
            ExitItem
        );

        if (picked == OpenItem)
        {
            ShowFromTray();
        }
        else if (picked == ExitItem)
        {
            Exit();
        }
    }

    private void HideWhenMinimized()
    {
        // Minimising Goes Straight To The Tray
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        // Closing Asks First, The Page Shows The Dialog
        if (!_isExiting)
        {
            eventArgs.Cancel = true;
            if (CloseRequested is null)
            {
                Hide();
            }
            else
            {
                _ = CloseRequested.Invoke();
            }
        }

        base.OnClosing(eventArgs);
    }
}
