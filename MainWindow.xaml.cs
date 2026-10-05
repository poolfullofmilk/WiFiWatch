using System.ComponentModel;
using System.Windows;
using WifiWatch.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WifiWatch;

public partial class MainWindow : Window
{
    private const int BalloonMilliseconds = 5000;

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

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => Exit());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "WifiWatch",
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
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

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void HideWhenMinimized()
    {
        // Minimising Goes To The Tray Like Closing
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        // Closing Hides, The Monitor Keeps Watching
        if (!_isExiting)
        {
            eventArgs.Cancel = true;
            Hide();
        }

        base.OnClosing(eventArgs);
    }

    private void Exit()
    {
        _isExiting = true;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
