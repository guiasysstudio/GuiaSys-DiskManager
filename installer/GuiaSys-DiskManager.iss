; GuiaSys Disk Manager - Inno Setup 6
#define AppName "GuiaSys Disk Manager"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#define AppPublisher "GuiaSys Studio"
#define AppExeName "GuiaSys.DiskManager.exe"
[Setup]
AppId={{A388E04F-84F3-41B6-9CA2-8AC047975D46}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName}
DefaultDirName={autopf}\GuiaSys Disk Manager
DefaultGroupName=GuiaSys Disk Manager
OutputDir=..\artifacts\setup
OutputBaseFilename=GuiaSys-DiskManager-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\GuiaSys.DiskManager\Assets\Branding\GuiaSys-DiskManager.ico
WizardStyle=modern
DisableProgramGroupPage=yes
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\GuiaSys Disk Manager"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\GuiaSys Disk Manager"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"
[Run]
Filename: "{app}\{#AppExeName}"; Description: "Executar GuiaSys Disk Manager"; Flags: nowait postinstall skipifsilent
