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

## GDM-C17 — Verificação de versões, empacotamento e integridade

- A versão fonte continua sendo `Directory.Build.props`; alinhe Version/AssemblyVersion/FileVersion/InformationalVersion antes de qualquer release estável.
- `tools/Build-Installer.ps1` rejeita versões inválidas, valida `FileVersion` do executável e exige instalador x64 versionado após compilar no Inno Setup.
- `tools/Write-PackageChecksums.ps1` gera `artifacts/checksums/SHA256SUMS.txt` (SHA256 do .exe portátil e Setup versionado); `tools/Test-PackageChecksums.ps1` verifica nomes exatos, duplicatas, hashes e arquivos.
- CI executa ambos após Inno e sobe `GuiaSys-DiskManager-win-x64-sha256`, além de `win-x64-portable` e `win-x64-setup`.
- A aplicação **somente** consulta releases públicas oficiais via HTTPS e abre a página validada no navegador. Não baixa nem executa instaladores; repositório privado não possui consulta pública autenticada automática.
- Antes de publicar para clientes, exigir assinatura de código e instalação/atualização/desinstalação testadas em Windows 10/11, incluindo perfil do usuário e licença. Não publicar versão comercial somente com CI.
- Ver [GDM-C17](GDM-C17.md) para o protocolo manual de release e limites de segurança.
