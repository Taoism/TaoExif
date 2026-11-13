; TaoExif installer (Inno Setup 6). Built by build-release.ps1:
;   ISCC /DAppVersion=0.1.1 /DSourceDir=..\publish\win-x64 /O..\release installer\taoexif-setup.iss

#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#define AppUrl "https://github.com/Taoism/TaoExif"

[Setup]
; keep this AppId: it lets new versions upgrade older TaoExif installs in place
AppId={{5B7E2C41-9A3D-4E8F-B6C2-7D1A0F93E4B8}
AppName=TaoExif
AppVersion={#AppVersion}
AppVerName=TaoExif {#AppVersion}
AppPublisher=Taoism
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\TaoExif
DefaultGroupName=TaoExif
DisableProgramGroupPage=yes
AllowNoIcons=yes
; installs for the current user without an administrator prompt; the dialog offers all users
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\TaoExif\Assets\taoexif.ico
UninstallDisplayIcon={app}\TaoExif.exe
UninstallDisplayName=TaoExif
LicenseFile=..\LICENSE
OutputBaseFilename=TaoExif-{#AppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
; close a running TaoExif (and its exiftool) before replacing files
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "explorermenu"; Description: "Add ""Open in TaoExif"" to the folder right-click menu"; GroupDescription: "Explorer:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; files from an older bundled ExifTool
Type: filesandordirs; Name: "{app}\exiftool"

[Icons]
Name: "{group}\TaoExif"; Filename: "{app}\TaoExif.exe"
Name: "{group}\{cm:UninstallProgram,TaoExif}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TaoExif"; Filename: "{app}\TaoExif.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Directory\shell\TaoExif"; ValueType: string; ValueName: ""; ValueData: "Open in TaoExif"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\TaoExif"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\TaoExif.exe"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\TaoExif\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TaoExif.exe"" ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\TaoExif"; ValueType: string; ValueName: ""; ValueData: "Open in TaoExif"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\TaoExif"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\TaoExif.exe"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\TaoExif\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TaoExif.exe"" ""%V"""; Tasks: explorermenu

[Run]
Filename: "{app}\TaoExif.exe"; Description: "{cm:LaunchProgram,TaoExif}"; Flags: nowait postinstall skipifsilent
