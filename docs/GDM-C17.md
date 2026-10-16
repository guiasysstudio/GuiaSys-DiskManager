# GDM-C17 — Instalador, verificação de versões e distribuição Windows

**Data:** 16/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C16.

## Escopo implementado

O C17 estabelece um fluxo de **verificação manual e conservadora de atualizações**, sem executar pacotes remotos dentro do aplicativo, e reforça a geração do instalador Inno Setup e os artefatos do CI. A identificação de release GitHub é **somente metadados públicos**: nenhuma licença, token GitHub, processo de armazenamento ou elevação é usada nessa etapa.

### Verificação no aplicativo

No diálogo **Sobre**, o botão **Verificar atualização** usa uma chamada HTTPS com timeout de 12 segundos para:

`GET https://api.github.com/repos/guiasysstudio/GuiaSys-DiskManager/releases/latest`

- O cliente HTTP do aplicativo **proíbe redirecionamentos automáticos**, cookies e autenticação. Só envia User-Agent e Accept da API.
- O JSON é limitado a 128 KiB (cabeçalho e leitura efetiva). Falhas HTTP, 401/403, timeout, resposta malformada, tamanho inválido e redirecionamento são **indisponível**, nunca gatilhos de execução.
- A release precisa ser **estável** (não draft, não prerelease), usar a tag estrita `vMAJOR.MINOR.PATCH` e possuir o instalador em estado `uploaded`, com nome exato `GuiaSys-DiskManager-Setup-MAJOR.MINOR.PATCH-x64.exe`, tamanho plausível e URL exatamente no repositório oficial.
- `html_url` deve coincidir com `https://github.com/guiasysstudio/GuiaSys-DiskManager/releases/tag/vMAJOR.MINOR.PATCH`. Nome, tag, link externo, metadados incompletos ou divergência de origem não são aceitos.
- A versão instalada é comparada numericamente usando a versão assembly de três componentes; builds mais recentes exibem o botão **Abrir versão oficial**, cujo destino é a **página já validada da release**. Não há download automático, instalação silenciosa, execução de arquivos externos, automodificação, bypass UAC ou escrita no disco gerenciado pelo programa.
- A ausência de release pública (HTTP 404, inclusive para repositório privado) é explicitamente informada. **A conexão GitHub do desenvolvedor não é compartilhada com os computadores dos clientes** e nunca deve ser embutida no executável.

**Limite importante:** validar URL e metadados de lançamento **não comprova assinatura digital do executável**. O usuário deve verificar a origem e, quando disponível, uma assinatura Authenticode válida e checksum vindo de canal confiável. Publicação comercial automatizada está condicionada à assinatura e aos critérios C18.

### Instalador, atualização e desinstalação

- `AppId` Inno Setup **mantido**, preservando a identidade instalada entre versões.
- `AppVersion`, `VersionInfoVersion` e nome `Setup-<version>-x64.exe` derivam de `Directory.Build.props`; apenas releases com SemVer estável `X.Y.Z` são admitidos na preparação do instalador.
- `Build-Installer.ps1` valida a versão principal/secundária/build do executável antes de empacotar, exige arquivo portátil e resultado Inno com tamanho plausível, e interrompe o build quando há inconsistência.
- O instalador é x64, possui ícone, idioma pt-BR, desinstalador, atalhos opcionais e modo de instalação com privilégios. Usa o Restart Manager para pedir fechamento de processos durante uma atualização, **sem reiniciar o aplicativo automaticamente**. Deve-se encerrar o programa antes de atualizar.
- Arquivos do aplicativo ficam em `{app}`; **logs, estado de licença e dados por usuário em `%LOCALAPPDATA%\GuiaSys\DiskManager` ficam fora do escopo do desinstalador**. Não se apaga a pasta do usuário ao desinstalar ou atualizar. O comportamento precisa ser verificado em Windows real, especialmente instalações por usuário vs. por máquina.
- Nenhum downgrade é bloqueado por lógica própria no C17: validar política de downgrade e migração de schema permanece gate de produto do C18. Não afirmamos rollback garantido.

### Artefatos e integridade no CI

A pipeline Windows existente continua executando restore, parser PowerShell, compilação e testes **Debug/Release**, `dotnet publish` win-x64 self-contained e compilação do Inno Setup. O C17 adiciona:

1. `tools/Write-PackageChecksums.ps1`: gera `artifacts/checksums/SHA256SUMS.txt` (UTF-8 sem BOM) contendo exatamente o SHA-256 do `GuiaSys.DiskManager.exe` portátil e do instalador com versão.
2. `tools/Test-PackageChecksums.ps1`: relê o manifesto, aceita somente os dois nomes esperados, rejeita duplicatas, hashes malformados e nomes fora da allowlist, compara todos os arquivos locais e falha quando há divergência.
3. Artefato do Actions `GuiaSys-DiskManager-win-x64-sha256`, além dos artefatos `-portable` e `-setup`, publicado **somente após as verificações**.

O manifesto SHA-256 gerado junto do binário detecta corrupção/alteração acidental, **mas não autentica o editor se o próprio manifesto puder ser trocado**. Exigir cadeia de confiança separada e assinatura Authenticode para distribuição comercial.

### Publicação de uma nova versão (manual, gate de C18)

1. Homologar funcionalidade/smoke, licenciamento, segurança, compatibilidade de atualização e identidade da release; **não usar este C17 como liberação comercial**.
2. Atualizar e revisar `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`) e confirmar tag `vX.Y.Z`. Fazer build do SHA exato homologado.
3. Conferir `0 warnings/0 errors`, Debug e Release, installer, portable, checksums e correspondência de nomes/versão. Executar `Get-FileHash -Algorithm SHA256` e comparar ao manifesto.
4. Após assinatura/controle C18 e aprovação humana, publicar **manualmente** uma release pública estável no repositório oficial, anexando instalador exatamente `GuiaSys-DiskManager-Setup-X.Y.Z-x64.exe` e arquivo de checksums pelo canal aprovado. Não marcar como draft/prerelease se a intenção for detectá-la por `releases/latest`.
5. No aplicativo, **Sobre → Verificar atualização → Abrir versão oficial**. Fechar o aplicativo antes de instalar manualmente. Preservar arquivos de usuário. Fazer smoke após atualização e verificar desinstalação e possível downgrade.

**O C17 não cria tag, não publica GitHub Release nem usa credenciais de GitHub em clientes.** Se o repositório continuar privado, a checagem anônima no cliente retornará indisponível/sem release pública; para distribuição comercial privada é necessária API própria autenticada e assinaturas verificáveis (planejamento C18).

## Evidência e limites de aceitação

- Testes `ReleaseUpdateCheckerTests`: comparação numérica, metadata confiável, pré-release/draft, asset errado, URLs externas, semver inválida, versões ausentes, falhas HTTP, metadata excessiva, JSON inválido e cancelamento.
- GitHub Actions no **HEAD final** deve concluir compilação/testes Debug + Release, publish Windows x64, instalação Inno e SHA-256 com resultado `success`. Contagem e logs precisam ser lidos do run do próprio SHA; builds intermediários falhos não são aprovação.
- Ainda faltam homologação de instalação/desinstalação/upgrade real em Windows 10 e Windows 11 (per-user, per-machine), assinatura Authenticode e SmartScreen, verificação de pacote recebido, testes de rollback e testes com release pública real.
- Nenhuma mudança em `StorageWriteGate`: **NO-GO para gravação física e distribuição comercial** até auditoria C18.

**Próximo módulo:** GDM-C18 — Auditoria e preparação de lançamento comercial.
