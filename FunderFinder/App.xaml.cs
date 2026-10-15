using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace FunderFinder;

public partial class App : Application
{
    // A second launch (e.g. clicking a pinned icon) signals the running instance to
    // come to front and then exits, rather than opening a duplicate panel.
    private const string MutexName = "FunderFinder-4C2E8A1D-SingleInstance";
    private const string BringToFrontEventName = "FunderFinder-4C2E8A1D-BringToFront";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _bringToFrontEvent;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Safety net: a bug in a click handler or animation shouldn't take down a tray app
        // that's meant to sit running all day. Report it and carry on.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            _mainWindow?.ReportError(args.Exception);
        };

        _instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(BringToFrontEventName);
                existingEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The other instance is still starting up; nothing to signal yet.
            }
            Shutdown();
            return;
        }

        _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BringToFrontEventName);
        StartBringToFrontListener();

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        SetupTrayIcon();
    }

    private void StartBringToFrontListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                _bringToFrontEvent!.WaitOne();
                Dispatcher.BeginInvoke(BringToFront, DispatcherPriority.Normal);
            }
        })
        { IsBackground = true };
        thread.Start();
    }

    private void SetupTrayIcon()
    {
        // The exe's embedded icon (Assets\icon.ico, set as ApplicationIcon in the .csproj).
        var icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "FunderFinder.exe");

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Bring to front", null, (_, _) => BringToFront());
        menu.Items.Add("Hide", null, (_, _) => HideWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Funder Finder",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) BringToFront(); };
        _trayIcon.BalloonTipClicked += (_, _) => BringToFront();
    }

    private void BringToFront() => _mainWindow?.BringToFront();

    /// <summary>Shows a small tray balloon — e.g. "a scan found new calls".</summary>
    public void ShowNotification(string title, string text)
        => _trayIcon?.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);

    private void HideWindow()
    {
        _mainWindow?.SaveNow();
        _mainWindow?.Hide();
    }

    public void ExitApp()
    {
        _mainWindow?.SaveNow();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _bringToFrontEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        Shutdown();
    }
}
