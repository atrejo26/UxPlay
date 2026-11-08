; Inno Setup script for the UxPlay Windows installer.
; Built by .github/workflows/windows-app.yml from the bundle in windows\out\UxPlay:
;     iscc /DAppVersion=1.74 windows\installer.iss
; Output: windows\out\UxPlay-Setup.exe

#ifndef AppVersion
  #define AppVersion "0.0"
#endif

[Setup]
AppId={{6C1E2B8A-4D7F-4C55-9B31-7E0F2A9D5C14}
AppName=UxPlay
AppVersion={#AppVersion}
AppVerName=UxPlay {#AppVersion}
AppPublisher=UxPlay (open source)
AppPublisherURL=https://github.com/atrejo26/UxPlay
DefaultDirName={autopf}\UxPlay
DefaultGroupName=UxPlay
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
; admin rights are needed to add the Windows Firewall rule
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=tray\uxplay.ico
UninstallDisplayIcon={app}\UxPlay.exe
UninstallDisplayName=UxPlay
CloseApplications=force
RestartApplications=no
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=out
OutputBaseFilename=UxPlay-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start UxPlay &automatically when I sign in to Windows"; GroupDescription: "Start-up:"; Flags: unchecked

[InstallDelete]
; an update replaces the whole receiver folder, so plugins dropped from a newer
; GStreamer don't linger and get loaded with the new DLLs
Type: filesandordirs; Name: "{app}\uxplay"

[Files]
Source: "out\UxPlay\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\UxPlay"; Filename: "{app}\UxPlay.exe"; Comment: "Show your iPhone screen on this PC"
Name: "{autodesktop}\UxPlay"; Filename: "{app}\UxPlay.exe"; Comment: "Show your iPhone screen on this PC"; Tasks: desktopicon

[Registry]
; same value the tray app's "Start UxPlay when I sign in" setting uses
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "UxPlay"; ValueData: """{app}\UxPlay.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; let the iPhone reach UxPlay through Windows Firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""UxPlay"""; Flags: runhidden; StatusMsg: "Configuring Windows Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""UxPlay"" dir=in action=allow enable=yes profile=any program=""{app}\uxplay\bin\uxplay.exe"""; Flags: runhidden; StatusMsg: "Configuring Windows Firewall..."
Filename: "{app}\UxPlay.exe"; Description: "Start UxPlay now"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM UxPlay.exe"; Flags: runhidden; RunOnceId: "StopTray"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""UxPlay"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"

[UninstallDelete]
; GStreamer's plugin cache; settings in the same folder are kept
Type: files; Name: "{localappdata}\UxPlay\gst-registry.bin"
