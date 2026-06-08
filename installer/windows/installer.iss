; installer/windows/installer.iss
; Inno Setup 6 script — ReControl Desktop Windows installer
; Per-machine (Program Files), UAC elevation, x64-only, unsigned (D-08/D-09/D-10)
;
; Build: run from recontrol_desktop/ via scripts\build-windows.ps1
; Manual: ISCC.exe installer\windows\installer.iss  (from recontrol_desktop/)

#define AppName      "ReControl Desktop"
#define AppVersion   "1.0.0"
#define AppPublisher "ReControl"
#define AppExeName   "ReControl.Desktop.exe"
#define PublishDir   "..\..\publish-win"

[Setup]
AppId={{E7F3A2B6-94D1-4C8E-B035-2F9A7E5D1C3A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\..\dist
OutputBaseFilename=ReControl-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#PublishDir}\{#AppExeName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
