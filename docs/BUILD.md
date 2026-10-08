# Build e publicação

## Requisitos

- Windows 10/11 x64;
- SDK .NET 10;
- Inno Setup 6 somente para o instalador.

## Gates locais

```powershell
dotnet clean .\GuiaSys.DiskManager.slnx
dotnet restore .\GuiaSys.DiskManager.slnx
dotnet build .\GuiaSys.DiskManager.slnx -c Debug --no-restore
dotnet test .\GuiaSys.DiskManager.slnx -c Debug --no-build --no-restore
dotnet build .\GuiaSys.DiskManager.slnx -c Release --no-restore
dotnet test .\GuiaSys.DiskManager.slnx -c Release --no-build --no-restore
dotnet publish .\src\GuiaSys.DiskManager\GuiaSys.DiskManager.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\portable
```

Execute `tools\Build-Installer.ps1` depois da publicação. A versão é lida de `Directory.Build.props` e passada ao Inno Setup.

## Identidade visual

`GuiaSys-DiskManager-Logo.png` é o original preservado. `tools\Generate-Branding.ps1` gera PNGs de 16 a 512 px e um ICO com dez resoluções sem redesenhar a marca.
