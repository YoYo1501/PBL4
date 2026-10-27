
; NetworkAdmin Server Installer
; PBL4 - NetworkAdminTool

#define MyAppName "NetworkAdmin Server"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NetworkAdminTool"
#define MyAppExeName "NetworkAdmin.Server.exe"

[Setup]
AppId={{D53EC220-A852-4614-8788-347E7A02AC61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Default installation directory
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Application icon in Windows uninstall list
UninstallDisplayIcon={app}\{#MyAppExeName}

; Windows 64-bit
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Installation settings
PrivilegesRequired=admin
DisableProgramGroupPage=yes

; Installer output
OutputDir=D:\1.University\Semester-5\Sanh\PBL4\NetworkAdminTool\NetworkAdminTool\Releases
OutputBaseFilename=NetworkAdmin_Server_Setup

; Compression
Compression=lzma
SolidCompression=yes

; Installer appearance
WizardStyle=modern dynamic

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "D:\1.University\Semester-5\Sanh\PBL4\NetworkAdminTool\NetworkAdminTool\publish\Server\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch NetworkAdmin Server"; Flags: nowait postinstall skipifsilent
