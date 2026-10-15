using System.Runtime.InteropServices;

namespace FunderFinder.Mac.Platform;

/// <summary>
/// The red-dot style number on the app's Dock icon (macOS only; does nothing elsewhere).
/// Avalonia has no API for this, so it calls AppKit directly:
/// <c>NSApplication.sharedApplication.dockTile.badgeLabel = "3"</c>.
/// Must be called on the UI thread.
/// </summary>
internal static class DockBadge
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    /// <summary>Shows <paramref name="count"/> on the Dock icon; 0 clears it.</summary>
    public static void Set(int count)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            var dockTile = Send(app, sel_registerName("dockTile"));
            var label = count <= 0 ? IntPtr.Zero : NSString(count > 99 ? "99+" : count.ToString());
            Send(dockTile, sel_registerName("setBadgeLabel:"), label);
        }
        catch
        {
            // A badge is a nicety; never let it take the app down.
        }
    }

    private static IntPtr NSString(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try { return Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
}
