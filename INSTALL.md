# Installing Funder Finder

Funder Finder is built from source with the free .NET 8 SDK. Building it takes a couple of minutes.

- [Windows](#windows)
- [macOS](#macos)
- [Troubleshooting](#troubleshooting)

---

## Windows

**Needs:** Windows 10 or 11, and the .NET 8 SDK.

### 1. Get the code

```powershell
git clone https://github.com/BJolly-97/funder-finder.git
cd funder-finder
```

No git? On the GitHub page, click **Code → Download ZIP** and extract it.

### 2. Install the .NET 8 SDK (if you don't have it)

Check first:

```powershell
dotnet --list-sdks
```

If no `8.x` line appears:

- **With admin rights:** `winget install Microsoft.DotNet.SDK.8`, or use the installer from
  https://dotnet.microsoft.com/download/dotnet/8.0
- **Without admin rights:** this installs into your own user folder.

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
  & "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
  $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
  ```

  To make the PATH change permanent, add that last line to your PowerShell profile (`notepad $PROFILE`).

### 3. Build

```powershell
cd FunderFinder_Windows
dotnet build -c Release
```

The app is now at `FunderFinder_Windows\bin\Release\net8.0-windows\FunderFinder.exe`.

### 4. Run, pin and add a desktop shortcut

- **To run it:** double-click `FunderFinder.exe`.
- **To make a desktop shortcut:** right-click the exe → *Send to* → *Desktop (create shortcut)*.
- **To pin it:** right-click the shortcut → *Pin to taskbar* (on Windows 11, you may need *Show more
  options* first). Windows doesn't let apps pin themselves, so this step is manual.

Once pinned, the pinned icon is also the running app's taskbar button. New calls appear on it as a
green number badge.

### 5. Start quietly with Windows (optional)

1. Press **Win+R**, type `shell:startup` and press Enter.
2. Right-click in that folder → *New* → *Shortcut*, and browse to `FunderFinder.exe`.
3. Right-click the new shortcut → *Properties*, and add ` --background` at the end of **Target**
   (after the closing quote, if there is one).

The app then starts minimised on the taskbar at login, scans at 9:00, and badges the icon when there's
something new. To undo this, delete the shortcut.

---

## macOS

**Needs:** macOS 11 (Big Sur) or later, on Apple Silicon or Intel. You also need the .NET 8 SDK to
build it; the finished app runs without it.

> The Mac version was developed on Windows. Avalonia lets its window be built and tested there, but the
> Mac-only parts haven't been tried on a real Mac yet: the Dock badge, the menu-bar icon, the `.app`
> packaging and the login item. If something misbehaves, please open an issue.

### 1. Get the code

```bash
git clone https://github.com/BJolly-97/funder-finder.git
cd funder-finder/FunderFinder_MacOS
```

### 2. Install the .NET 8 SDK

Download the **.NET 8 SDK macOS installer** from https://dotnet.microsoft.com/download/dotnet/8.0. Choose
**Arm64** for Apple Silicon (M1 and later) or **x64** for Intel. Then check it's installed:

```bash
dotnet --list-sdks
```

### 3. Build the app

```bash
chmod +x build-macos-app.sh login-item.sh
./build-macos-app.sh
```

This creates `dist/Funder Finder.app`, a self-contained app for your Mac's chip, with its icon. Drag it
into **Applications**.

**First launch:** the app isn't signed with an Apple developer certificate, so macOS may say it can't
verify the developer. **Right-click → Open** once, then confirm. It opens normally after that.

### 4. Using it on a Mac

- **Closing the window** (red button) minimises it to the Dock. Funder Finder keeps running so it
  can scan at 9:00.
- **To quit:** use the **menu-bar icon → Quit Funder Finder**, or **⌘Q**.
- **New calls** after a scan you weren't watching show as a number on the **Dock icon**. The number
  clears when you open the window.
- **⌘F** jumps to search, and **Esc** clears it.

### 5. Start quietly at login (optional)

```bash
./login-item.sh install     # starts minimised at login, scans at 9:00
./login-item.sh remove      # undo
```

This uses a per-user LaunchAgent. macOS's own Login Items setting can't pass the `--background` flag.

### Just trying it out?

You can run it straight from the source without building an app:

```bash
dotnet run -c Release
```

---

## Troubleshooting

- **A source says "Problems with: …" after a scan.** That funder has probably changed its website. The
  other sources still work. Its reader is one file in `FunderFinder_Windows/Services/Sources/`.
- **Nothing shows in Opportunities.** Check **Filters → Sources** (are they ticked?) and **Filters →
  Keywords**. Tick *Include non-matching* to see everything the scan found.
- **Moving to another computer.** Copy `data.json` (see the README for where it lives) to the same place
  on the new machine, with the app closed on both.
- **Windows: `dotnet` says no SDKs were found.** Your PATH is pointing at a runtime-only .NET. Use the
  user-folder install above and put it first on your PATH.
- **macOS: "Funder Finder is damaged and can't be opened".** This happens if the app was downloaded
  rather than built on your Mac. Run `xattr -dr com.apple.quarantine "/Applications/Funder Finder.app"`
  in Terminal, then open it again.
