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

## Gate de comercialização GDM-C18 (NÃO libera produção)

Após `dotnet test`, publish win-x64, Inno Setup e verificação do manifesto SHA-256, execute:

```powershell
.\tools\Test-CommercialReleaseGate.ps1
Get-Content .\artifacts\readiness\commercial-readiness.json
# Opcional: comprovar bloqueio comercial; o comando DEVE falhar nesta branch:
.\tools\Test-CommercialReleaseGate.ps1 -RequireCommercialGo
```

O CI de desenvolvimento registra o relatório como artefato `GuiaSys-DiskManager-C18-NO-GO-audit` e considera correto o `NO-GO`; um `success` no CI não permite publicar uma versão comercial. O modo obrigatório deve integrar um workflow de distribuição **separado**, com assinatura e evidência humana auditada. Não invente dados da licença, não habilite `StorageWriteGate` e não rode testes de escrita em discos físicos. Ver [GDM-C18](GDM-C18.md).

## GDM-H01 — Pré-verificação Windows x64, somente arquivos

Extraia o portátil, instalador e `SHA256SUMS.txt` do **mesmo run/commit** para `artifacts/portable`, `artifacts/setup`, `artifacts/checksums` e execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-WindowsReadOnlyPreflight.ps1
Get-Content .\artifacts\smoke\windows-readonly-preflight.json
```

O script valida hashes, versões e Authenticode **sem executar nenhum pacote, sem solicitar administração e sem alterar discos**. É permitido apontar os caminhos explicitamente com `-PortablePath`, `-InstallerPath`, `-ManifestPath` e `-OutputPath`. Para exigir duas assinaturas Authenticode válidas (atualmente pendentes), use `-RequireValidSignatures` e espere erro neste build de desenvolvimento. SHA256 de arquivo e manifesto baixados do mesmo local **não garante identidade do editor**.

A pipeline usa ainda uma fixture de manifesto adulterado para provar falha explícita e publica `GuiaSys-DiskManager-H01-readonly-preflight`. Instalação e smoke real no Windows 10/11 continuam **fora da execução do CI**. Consulte [GDM-H01](GDM-H01.md).

## GDM-H02 — Pré-verificação offline de confiança GuiaSys Licensing

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-GslV1TrustReadiness.ps1
Get-Content .\artifacts\licensing\gsl-v1-readiness.json
# Deve falhar até haver dados reais, origem independente e homologação:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-GslV1TrustReadiness.ps1 -RequireProductionTrust
```

Não executa requests nem ativa licenças; audita somente constantes públicas e a presença de `gsl-v1-trust.json` no build. Sem fingerprint fixado, a pasta `artifacts/portable` não pode conter uma configuração pública não aprovada. O GitHub Actions faz a verificação e publica `GuiaSys-DiskManager-H02-GSL-NO-GO`. Veja [GDM-H02](GDM-H02.md) para obter e aprovar os **parâmetros públicos** e organizar ensaios reais autorizados; nunca colocar chaves privadas, contas admin ou licenças em commits.

## GDM-H02.1 — Inspeção offline de chave pública (não aprova produção)

A ferramenta `tools/GslV1TrustInspector` reutiliza o parser estrito do app, compila como console .NET 10 e não executa requisições ao Licensing/Firebase. Para usar com JWK **público** já conferido no painel:

```powershell
dotnet run --project .\tools\GslV1TrustInspector\GslV1TrustInspector.csproj -c Release -- --self-test

dotnet run --project .\tools\GslV1TrustInspector\GslV1TrustInspector.csproj -c Release -- ` 
  --file "<CAMINHO_ARQUIVO_JSON_PUBLICO>" ` 
  --expected-project-id "<PROJECT_ID_CONFERIDO>" ` 
  --expected-key-id "<KEY_ID_CONFERIDO>"
```

O resultado é sempre `decision: NO-GO`, mesmo com hash válido, porque origem e assinatura do editor não podem ser inferidas apenas de um arquivo. O autoteste e recusa de arquivo inexistente são executados no Actions. **O comando não faz pin automático, não altera dados produtivos nem libera licenças.** Veja [GDM-H02.1](GDM-H02.1.md).

## GDM-H02.1 — Novo domínio oficial e âncora pública GSL-v1

`GslV1Protocol.ApiBase` usa agora `https://licencas.guiasys.com.br/`. O `gsl-v1-trust.json` público foi incorporado a `src/GuiaSys.DiskManager/Licensing` e sai ao lado de `GuiaSys.DiskManager.exe` após o `dotnet publish`; também entra no Setup pelo wildcard do Inno. O digest SHA-256 de `GSL-v1\nprojectId\nkeyId\nx\ny` foi conferido e fixado no código, sem chave privada. CI exige que arquivo distribuído, projeto, `kid`, curva e digest correspondam ao valor esperado. `tools/Test-GslV1TrustReadiness.ps1` mantém **NO-GO** mesmo com pin: não é teste da licença real. Ver [GDM-H02.1](GDM-H02.1.md).
