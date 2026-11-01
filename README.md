# R - Wallpaper Studio

A Windows wallpaper app with a native WinUI 3 interface, saved locations, a wallpaper carousel, video wallpapers and a tray menu.

## Downloads

Choose one package from the GitHub Releases page:

- **Setup.exe** installs the app for your Windows account, adds a Start menu shortcut and an uninstaller. A desktop shortcut is optional. Open the app, choose your wallpapers and click Apply. The background service starts with Windows after Apply.
- **portable.zip** requires no installation. Extract the entire ZIP to a writable folder and open **WallpaperStudio.exe**. Keep all files together. Settings are stored in the `Data` folder beside the executable. Portable mode does not add a Windows startup entry. Open it and click Apply each time you want to start the wallpaper service after restarting Windows.

Do not run installed and portable wallpaper services at the same time. Exit the old copy using its tray menu before switching. The portable package is one downloadable ZIP, not a standalone executable: WinUI and media dependencies must stay beside the app.

## Requirements and use

Windows 10 version 2004 or newer, or Windows 11, on an x64 PC. The .NET and Windows App SDK runtimes are included. The map requires Microsoft Edge WebView2 Runtime; Setup installs it if missing (internet required). For portable use, install [Microsoft WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) if it is not already present. Windows location services and internet connectivity are required for location-based switching and online map tiles.

First launch reads your current desktop wallpaper and starts with no saved locations. Choose your images, add places on the map and click Apply. No personal photos, saved locations or developer configuration are included. Your image/video files stay at their original paths, so keep them available; moving a portable folder does not move external wallpaper files.

The settings window closes only with Close or the title-bar close button. Applying settings keeps it open. A lightweight tray service remains running to change wallpapers; exit it from the tray to stop automation. Ctrl+Alt+P toggles default-wallpaper mode. Videos play muted on the desktop; the lock screen uses a still frame. Windows cannot run this per-user app before the first sign-in after a cold boot; the previously set lock-screen image remains until it starts.

Background location, network monitoring, static wallpaper and lock-screen updates run in `WallpaperWorker.exe` without WinUI or the map. The WinUI video player starts only for active video playback and exits when playback stops, including static-wallpaper, carousel, privacy and lock transitions. Closing settings releases its map/browser process. Monitoring intervals and wallpaper rules are unchanged; memory use varies with playback and media size.

The tray context menu uses Windows 11-style rounded corners, spaced icon rows, hover highlights and a separate keyboard shortcut hint. It follows the Windows taskbar light/dark setting when opened, supports high contrast, and preserves standard keyboard navigation. It runs inside the tray service without starting WinUI.

Location switching requires a Windows fix no older than 30 seconds, with reported accuracy of 150 metres or better; default-location and IP-only fixes are rejected. Startup, resume and network changes invalidate earlier fixes. Unavailable or imprecise location keeps the default wallpaper. Windows can still supply an incorrect position: this app cannot create GPS accuracy on hardware that lacks it. The map requests fresh fixes, removes stale pins, recreates failed browser instances and retries failed tiles while online.

Ctrl+Alt+P explicitly holds the default wallpaper until pressed again, including when returning to a saved place. Location tracking continues while this override is active. Normal sign-out/shutdown requests restore the default desktop immediately and give the worker up to four seconds to persist the configured default lock-screen image. Startup begins with defaults before accepting a new location. An abrupt power cut or forced process termination cannot run cleanup, and the per-user app cannot change Windows' pre-sign-in screen before the first sign-in.

Installed settings live in `%LOCALAPPDATA%\RWallpaperStudio`. Uninstall preserves them and your original media. Uninstall removes this installation's startup entry. Portable settings and browser cache live in `Data` and can be backed up with that folder.

These packages are unsigned. Windows may show an unknown-publisher warning. Public releases can be Authenticode-signed with the project owner's certificate; no certificate or signing identity is included.

## Build

Use Windows, the .NET 10 SDK, and Inno Setup 6.7 or newer. The .NET Framework compiler provided by Windows builds the tray service. From the source root:

```powershell
dotnet restore WinUI3Prototype/WallpaperStudio.csproj --configfile WinUI3Prototype/NuGet.Config
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/Build-Release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The release script publishes to a fresh staging directory, excludes test outputs and private assets, builds the installer and portable ZIP, exports a source ZIP and creates SHA-256 checksums. It downloads Microsoft's signed WebView2 bootstrapper if needed. Outputs are in `dist`.

## Publishing to GitHub

Extract the **Source.zip** into a new folder and upload that folder as the repository source. It is an explicit clean export; do not upload the entire development workspace. Create a `v2.1.0` GitHub release and attach **Setup.exe**, **portable.zip**, and **SHA256SUMS.txt**. The source archive is optional as a release asset. No repository is created or published automatically.

Choose a license for your own code before announcing it as open source. Third-party licenses remain in the distribution; see `THIRD-PARTY-NOTICES.md`.

Match Home Screen mirrors the current desktop both inside and outside saved places, and mirrors the default at shutdown. With Match Home off, Match Default selects the default wallpaper; with both off, the independent lock image is used. Default and independent lock pickers accept videos even with the location carousel enabled. Default videos play on the desktop; lock-screen videos use a generated still frame because Windows exposes an image-only lock-screen background API.
