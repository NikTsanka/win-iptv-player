; Inno Setup script for IPTV Player.
; Build a real Setup.exe with:
;   1. Install Inno Setup 6  (https://jrsoftware.org/isdl.php)
;   2. Run:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" IptvPlayer.iss
;   (or open this file in the Inno Setup IDE and press Build > Compile)
; The output Setup.exe appears in .\installer-output\
;
; NOTE: run `dotnet publish ... -o publish` first so the publish\ folder exists.

#define MyAppName "IPTV Player"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Nik Tsanka"
#define MyAppExeName "IptvPlayer.exe"

[Setup]
AppId={{8F3C2A91-6B4E-4D7A-9C12-IPTVPLAYER001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\IptvPlayer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=IptvPlayer-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; --- Branding ---
SetupIconFile=assets\icon.ico
WizardImageFile=assets\wizard-large.bmp
WizardSmallImageFile=assets\wizard-small.bmp
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; ~355 MB installed; require a little headroom
ExtraDiskSpaceRequired=10485760

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything produced by `dotnet publish` (exe, .NET runtime, libvlc + plugins).
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
