; Installer for Edge Dashboard (Inno Setup 6).
; Build with:  .\installer\build.ps1
; Installs for the current user (no admin prompt), starts with Windows, and tells Windows to
; run it on the power-saving GPU (the AMD integrated graphics that drive the Xeneon Edge).

#define AppName "Edge Dashboard"
#define AppVersion "1.0.0"
#define AppExe "EdgeDashboard.exe"

[Setup]
AppId={{6E1B5C0A-7C1D-4E55-9B63-2C8E0D5A4F11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=mohamedmoheyeldin
DefaultDirName={localappdata}\Programs\Edge Dashboard
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=EdgeDashboard-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=force
RestartApplications=no

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Start with Windows.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EdgeDashboard"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue
; Same as Settings > Display > Graphics > "Power saving": keeps the dashboard off the gaming GPU.
Root: HKCU; Subkey: "Software\Microsoft\DirectX\UserGpuPreferences"; ValueType: string; ValueName: "{app}\{#AppExe}"; ValueData: "GpuPreference=1;"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Description: "Start {#AppName}"
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopDashboard"

[UninstallDelete]
Type: files; Name: "{app}\error.log"
