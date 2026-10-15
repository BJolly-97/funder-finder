using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace FunderFinder.Mac;

public partial class App : Application
{
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Started at login with --background: open minimised to the Dock, no window over your work.
            var background = desktop.Args?.Contains("--background", StringComparer.OrdinalIgnoreCase) == true;
            _window = new MainWindow();
            if (background)
            {
                _window.WindowState = WindowState.Minimized;
                _window.ShowActivated = false;
            }
            _window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow() => _window?.BringToFront();

    private void Tray_Clicked(object? sender, EventArgs e) => ShowWindow();
    private void TrayShow_Click(object? sender, EventArgs e) => ShowWindow();
    private void TrayScan_Click(object? sender, EventArgs e) => _window?.ScanFromMenu();

    private void TrayQuit_Click(object? sender, EventArgs e)
    {
        if (_window is not null) _window.Quit();
        else (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
