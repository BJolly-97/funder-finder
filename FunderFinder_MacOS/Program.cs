using Avalonia;
using FunderFinder.Mac.Platform;
using PdfSharp.Fonts;

namespace FunderFinder.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // On a Mac, keep data where Mac apps do: ~/Library/Application Support/FunderFinder.
        // (The shared DataStore reads this before it first touches the disk.)
        if (OperatingSystem.IsMacOS() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUNDERFINDER_DATA_DIR")))
            Environment.SetEnvironmentVariable("FUNDERFINDER_DATA_DIR", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "FunderFinder"));

        // PDF export needs fonts from the system; point it at the Mac's before anything exports.
        GlobalFontSettings.FontResolver = new SystemFontResolver();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .LogToTrace();
}
