; GuiaSys Disk Manager - Inno Setup 6
; Publish first: dotnet publish src/GuiaSys.DiskManager/GuiaSys.DiskManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/portable
#define AppName "GuiaSys Disk Manager"
#define AppVersion "0.1.0"
[Setup]
AppId={{A388E04F-84F3-41B6-9CA2-8AC047975D46}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\GuiaSys Disk Manager
DefaultGroupName=GuiaSys Disk Manager
OutputDir=..\artifacts\setup
OutputBaseFilename=GuiaSys-DiskManager-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\GuiaSys.DiskManager.exe
WizardStyle=modern
[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\GuiaSys Disk Manager"; Filename: "{app}\GuiaSys.DiskManager.exe"
Name: "{autodesktop}\GuiaSys Disk Manager"; Filename: "{app}\GuiaSys.DiskManager.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"
[Run]
Filename: "{app}\GuiaSys.DiskManager.exe"; Description: "Executar GuiaSys Disk Manager"; Flags: nowait postinstall skipifsilent
