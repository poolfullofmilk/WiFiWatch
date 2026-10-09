using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using MudBlazor.Utilities;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Desktop.Interop;
using WiFiWatch.Desktop.Theming;
using WiFiWatch.Services.Monitoring;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WiFiWatch.Desktop;

public partial class MainWindow : Window
{
    private const int BalloonMilliseconds = 5000;
    private const string OpenItem = "Open";
    private const string ExitItem = "Exit";

    private readonly Forms.NotifyIcon _trayIcon;
    private bool _isExiting;

    // Tray Icons Per State, Drawn Once
    private readonly Drawing.Icon _plainIcon;
    private readonly Drawing.Icon _warningIcon;
    private readonly Drawing.Icon _criticalIcon;
    private string? _problemText;

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

        _plainIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;
        _warningIcon = WithDot(_plainIcon, ToDrawingColor(AppTheme.Custom.PaletteDark.Warning));
        _criticalIcon = WithDot(_plainIcon, ToDrawingColor(AppTheme.Custom.PaletteDark.Error));
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _plainIcon,
            Text = AppInfo.DisplayName,
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
            Dispatcher.InvokeAsync(() => _trayIcon.Text = _problemText ?? monitor.Status.TrayText);
        monitor.EventRecorded += () => Dispatcher.InvokeAsync(ShowProblemStateAsync);
        _ = Dispatcher.InvokeAsync(ShowProblemStateAsync);
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

    private async Task ShowProblemStateAsync()
    {
        // The Worst Open Problem Colours The Tray Icon
        var worst = (await EventJournal.OpenProblemsAsync()).FirstOrDefault();
        _trayIcon.Icon = worst?.Severity switch
        {
            EventSeverity.Critical => _criticalIcon,
            EventSeverity.Warning => _warningIcon,
            _ => _plainIcon,
        };
        _problemText = worst is null ? null : $"{AppInfo.DisplayName}, {worst.Kind.ToLabel()}";
    }

    private static Drawing.Icon WithDot(Drawing.Icon baseIcon, Drawing.Color color)
    {
        using var bitmap = baseIcon.ToBitmap();
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            // A Ringed Dot In The Corner, Readable At 16 Pixels
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var size = bitmap.Width / 2;
            var bounds = new Drawing.Rectangle(
                bitmap.Width - size - 1,
                bitmap.Height - size - 1,
                size,
                size
            );
            using var brush = new Drawing.SolidBrush(color);
            using var ring = new Drawing.Pen(Drawing.Color.FromArgb(0x14, 0x14, 0x14), size / 6f);
            graphics.FillEllipse(brush, bounds);
            graphics.DrawEllipse(ring, bounds);
        }

        // Ponytail: Two Icon Handles Live For The App's Life
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static Drawing.Color ToDrawingColor(MudColor color) =>
        Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

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
