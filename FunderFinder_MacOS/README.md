# Funder Finder for macOS

The Mac version of Funder Finder, built with [Avalonia](https://avaloniaui.net/). It looks and behaves
like the Windows app and uses **the same core code**. The sources, keyword matching, scanning, saved
data and PDF export are compiled in from `../FunderFinder_Windows/Models/` and
`../FunderFinder_Windows/Services/` (see `FunderFinder.Mac.csproj`).

To build and install it, see [INSTALL.md](../INSTALL.md#macos).

## What's Mac-specific

| | Windows app | This app |
|---|---|---|
| Window | Frosted panel, custom banner | Tan panel, custom banner; traffic lights at the banner's left |
| New-calls badge | Taskbar overlay | **Dock icon** number (`Platform/DockBadge.cs`, via AppKit) |
| Tray | System tray icon | **Menu-bar icon**: Show, Scan now, Quit |
| Close button | Minimise to taskbar | Minimise to the **Dock** (quit with ⌘Q or the menu-bar icon) |
| Start at login | Startup-folder shortcut with `--background` | LaunchAgent via `./login-item.sh install` |
| PDF fonts | Segoe UI | Arial from the system (`Platform/SystemFontResolver.cs`) |
| Data | `%AppData%\FunderFinder` | `~/Library/Application Support/FunderFinder` |

## Files

| File | What it does |
|---|---|
| `App.axaml(.cs)` | Palette, menu-bar icon, quiet start with `--background` |
| `Styles.axaml` | The look: pills, flat icon buttons, round ticks, rounded fields, leaf colours |
| `MainWindow.axaml(.cs)` | The window: tabs, ranked lists, search, filters, shortlist, PDF, scheduled scan |
| `MainWindow.Tree.cs` | The scan tree and the once-a-day replay |
| `ConfirmDialog.axaml(.cs)` | Themed "are you sure?" dialog |
| `Platform/` | Dock badge, PDF font lookup |
| `build-macos-app.sh` | Builds `dist/Funder Finder.app` (self-contained, with icon) |
| `login-item.sh` | Start quietly at login, or stop doing so |
| `Packaging/FunderFinder.iconset` | Icon images, turned into the `.icns` app icon at build time |

## Status

The window, lists, filters, search, shortlist and scan tree have been built and run with Avalonia on
Windows. The Mac-only pieces haven't been tried on a real Mac yet: the Dock badge, the menu-bar icon, the
`.app` bundle and the login item. They follow Apple's documented APIs and file layouts.

You can also run this build on Windows (`dotnet run -c Release`), which is handy for working on the UI.
Set `FUNDERFINDER_DATA_DIR` to a scratch folder first so it doesn't share the Windows app's data.
