#ifndef AppVersion
  #define AppVersion "2.1.0"
#endif
[Setup]
AppId={{B69E5EAD-AF2C-4EF5-9DC7-65A4E83B88C1}
AppName=R - Wallpaper Studio
AppVersion={#AppVersion}
AppPublisher=R - Wallpaper Studio
DefaultDirName={localappdata}\Programs\R - Wallpaper Studio
DefaultGroupName=R - Wallpaper Studio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayIcon={app}\WallpaperStudio.exe
SetupIconFile=..\app_icon.ico
OutputDir=..\dist
OutputBaseFilename=R-Wallpaper-Studio-{#AppVersion}-win-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\.release\installed\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Stop-InstalledApp.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Stop-InstalledApp.ps1"; Flags: dontcopy
Source: "..\.build-tools\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\R - Wallpaper Studio"; Filename: "{app}\WallpaperStudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\app_icon.ico"
Name: "{autodesktop}\R - Wallpaper Studio"; Filename: "{app}\WallpaperStudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\app_icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\WallpaperStudio.exe"; Description: "Open R - Wallpaper Studio"; Flags: nowait postinstall skipifsilent

[Code]
function HasWebView2: Boolean;
var Version: String;
begin
  Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if not HasWebView2 then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'Unable to start Microsoft Edge WebView2 installation. Install WebView2 Runtime and try again.'
    else if not HasWebView2 then
      Result := 'Microsoft Edge WebView2 Runtime is required for the map. Connect to the internet, install WebView2 Runtime, then try again.';
    if Result <> '' then Exit;
  end;
  ExtractTemporaryFile('Stop-InstalledApp.ps1');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Stop-InstalledApp.ps1') + '" -InstallDirectory "' + ExpandConstant('{app}') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Unable to close the installed app. Exit it from the tray and try again.'
  else if ExitCode <> 0 then Result := 'Unable to prepare the installed app for update. Exit it from the tray and try again.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ExitCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Stop-InstalledApp.ps1') + '" -InstallDirectory "' + ExpandConstant('{app}') + '" -Uninstall',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;
