# Status verificável — 0.1.0-dev

> **Reconstrução GDM-C00 (14/10/2026):** os gates abaixo referem-se à **base 0.1.0-dev testada em 08/10/2026**, não significam aprovação da integração comercial nem dos riscos P0. O baseline `fac02647` falhou no CI por `CS0246`; o C00 corrigiu o import e registrou sua própria validação em [GDM-C00](GDM-C00.md). Operações de escrita em hardware físico **não estão liberadas para teste nesta reconstrução**. A situação de cada HEAD deve ser verificada nos Actions, sem presumir o resultado histórico.

## IMPLEMENTADO

- inventário, seleção sincronizada, detalhes e mapa proporcional;
- fila e operações allow-listed de disco/partição/volume;
- `SafetyService`, confirmação reforçada, revalidação e logs;
- branding, About, publish x64, instalador, CI e integração VHDX opt-in sem dependência de Hyper-V.

## TESTADO

- build e suíte xUnit: 45/45 testes aprovados em Debug e 45/45 em Release, zero warnings;
- abertura real sem elevação e inventário do Windows registrado em log;
- publicação Release self-contained `win-x64` em arquivo único;
- instalador Inno Setup 6.7.3 compilado, instalação silenciosa por usuário, abertura do app instalado e desinstalação silenciosa aprovadas.
- gate administrativo aprovado em 08/10/2026: 32/32 etapas com dois VHDX descartáveis, cobrindo GPT/MBR, online/offline, somente leitura, criação, NTFS/FAT32/exFAT, labels, letras, redução, expansão e exclusão;
- CI verde no HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`.

## VALIDADO

- regras puras de segurança, fila, parsing, classificação e identidade por testes automatizados;
- inventário somente leitura em Windows local.
- estado antes/depois de cada escrita confirmado por processos PowerShell independentes; ambos os VHDX foram desmontados e excluídos no `finally`.
- pipeline GitHub Actions reproduzindo build/test Debug e Release, publish e instalador em runner limpo.

## FORA DO ESCOPO VALIDADO

- matriz física Windows 10/11, DPI e USB descartável;
- clonagem, recuperação avançada e telemetria SMART proprietária.

Nenhuma função fora do escopo validado é apresentada na interface como suporte disponível. O gate VHDX não está pendente.

## Reconstrução GDM-C00 — CI da base de código

O commit `c0a220c66f938067f7e3b48f24eb384f5f8a97c5` aprovou as execuções Actions `38666644551` e `38666648330`: restore; builds Debug/Release sem warnings ou errors; testes **87/87 em ambas as configurações**; publish win-x64; instalador Inno; upload dos dois artefatos. Isto não revalida o gate VHDX histórico nem autoriza uso comercial de gravação física. Detalhes e pendências: [GDM-C00](GDM-C00.md).

## Reconstrução GDM-C01 — licenciamento (14/10/2026)

O cliente desktop GSL-v1 implementa consultas HTTPS da configuração do produto, ativação, avaliação, revalidação, desativação, JWS ES256 validado contra chave de confiança fixada, ID de instalação persistente e cache criptografado DPAPI. A UI oferece a janela **Licença**.

**Separação de gates:** testes automatizados com HTTP e JWS simulados são evidência funcional do cliente, mas **não** equivalem a ativação na produção. O build atual **não inclui a âncora pública de confiança aprovada para este produto**, logo bloqueia a concessão de licença real. É necessário obter do GuiaSys Licensing e confirmar externamente `projectId`, `signing.keyId`, `signing.publicJwk` e incorporar o fingerprint ao build. Nenhum segredo privado deve entrar no cliente. Operações de escrita continuam bloqueadas até C02–C04. Consulte [GDM-C01](GDM-C01.md) e o GitHub Actions no HEAD atual.

## GDM-C02 — Núcleo de gerenciamento (15/10/2026)

- Novo modelo de geometria com intervalos seguros, espaços contíguos e falha fechada para interseções/intervalos inválidos.
- Inventário Windows e mapa visual agora compartilham esse núcleo; cálculo determinístico e sem escrita.
- `IOperationPlanner` testa operações contra o snapshot projetado e detecta conflitos de letras, disponibilidade de extensão contínua, transições de flags e dependências que exijam novo inventário.
- UI apresenta resumo de planejamento, condição do disco e erros de conflito, além de impedir a inclusão de dependências inválidas.
- Execução de escrita permanece bloqueada por `StorageWriteGate` de C01. Problemas P0 de fingerprint de partição, validação no executor e confirmação multi-disco **não foram declarados resolvidos**. Sem gate físico; testes de escrita apenas VHDX opt-in em etapa posterior.
- Evidências detalhadas: [GDM-C02](GDM-C02.md) e status do pipeline Actions no HEAD desta branch.

## GDM-C03 — Identidade e autorização de alvos (15/10/2026)

- Captura do GUID GPT e tipo GPT, comparação canônica de identidade/topologia em operações destinadas a partições.
- Identidade MBR insuficiente para escrita: recusada, sem exceção automática.
- Proteções para layout integral (incluindo operações de criar, excluir e redimensionar) e identificação de tipos GPT não básicos.
- Executor exige sua própria autorização (gate físico, inventário novo, SafetyService, administrador, licença), sem depender exclusivamente do ViewModel; processo PowerShell revalida disk UniqueId e atributos críticos da partição.
- Confirmação reforçada cobre todos os discos afetados e o sinalizador legado de confirmação não ignora o diálogo.
- Escritas físicas e testes destrutivos continuam **NO-GO** até os itens pendentes de C04. Relatório: [GDM-C03](GDM-C03.md).

## GDM-C04 (15/10/2026)

Execução com journal JSONL durável, pós-condições verificadas e distinção entre recusa, sucesso comprovado e resultado incerto. Runner VHDX bloqueado. Nenhuma gravação liberada. Ver [GDM-C04](GDM-C04.md).

## GDM-C05 — Nova interface WPF (15/10/2026)

- Interface principal reestruturada: cabeçalho global, sidebar navegável, visão geral, mapa proporcional, tabela e detalhes de partições, editor contextual e fila inferior redimensionável.
- Estilos dark-mode, foco via teclado, tooltips em ações desativadas, navegação por seções e atalho F5.
- `ContextOperationPolicy` analisa alvo selecionado com as mesmas recusas de `SafetyService`; nenhuma disponibilidade na interface autoriza execução real.
- `MainViewModel` passa a atualizar ações contextuais, estado do item selecionado, seleção refletida no mapa, métricas do inventário, e oculta dispositivos obsoletos em falha de refresh.
- `ApplyCommand` desabilitado com gate de escrita fechado. GSL-v1, execução e journal C04 preservados.
- Testes automatizados de decisão contextual e integração do ViewModel adicionados. Smoke visual manual no Windows ainda pendente.
- Relatório: [GDM-C05](GDM-C05.md). **NO-GO** para gravações físicas e testes destrutivos VHDX.

## GDM-C06 — Inventário e visualização avançada (15/10/2026)

- Consulta read-only de setor físico/lógico e `Get-Volume.Size`/`SizeRemaining` opcionais.
- Associação conservadora de `Get-PhysicalDisk` por serial único; mídia e temperatura desconhecidas não são fabricadas. NVMe é rotulado como protocolo, não prova de SSD.
- `InventoryInsights` calcula capacidade particionada, espaço não alocado, maior faixa contígua e volume utilizado, com prevenção de overflow.
- GUI mostra resumos globais, capacidade do disco, progresso de uso do volume, métricas opcionais, proteção e classificação GPT confiável.
- Parser testável com campos opcionais ausentes/inválidos e fixtures sem operações de gravação.
- Gate de escrita e testes destrutivos continuam **NO-GO**. Detalhes: [GDM-C06](GDM-C06.md).

## GDM-C07 — Operações essenciais (15/10/2026)

- `EssentialOperationValidator` exige contrato coerente por tipo: disco ou partição, GUID/identidade, rótulo, tamanho, letra, filesystem e cluster; proíbe parâmetros ocultos/stale.
- O `MainViewModel` prepara comandos somente com os campos relevantes, sem confundir entradas anteriores; valida letras de D a Z como um único caractere e requer tamanho no resize.
- `SafetyService` checa limites de área contígua diretamente no inventário recente, recusa GPT ambíguo e impede criação MBR sem pós-condição independente confiável.
- Testes sintéticos e de ViewModel foram adicionados; nenhum teste de disco físico/VHDX foi liberado.
- Gate **NO-GO para gravações**. Ver [GDM-C07](GDM-C07.md).

## GDM-C08 — Redimensionamento com limites do Windows (15/10/2026)

- Campos opcionais de limites `SizeMin/SizeMax` no inventário e consulta conservadora `Get-PartitionSupportedSize` para partições GPT elegíveis.
- `PartitionResizePolicy` combina faixa Windows, setor lógico, identificação GPT e espaço livre **contíguo imediatamente posterior** à partição. Sem dados confiáveis, nega planejamento.
- Painel WPF exibe limites e tamanho atual; presets preenchem tamanhos exatos em bytes e permanecem desabilitados quando indisponíveis. Alterações de capacidades após refresh não redirecionam automaticamente o usuário para outra operação.
- `SafetyService` valida os limites do snapshot de execução e script PowerShell reconsulta faixa suportada imediatamente antes do comando `Resize-Partition`. Nenhuma chamada de escrita foi feita.
- Cenários de teste sintéticos para extremos, alinhamento, geometry, VHDX simulados e limites desaparecidos. Smoke visual e VHDX real ainda pendentes.
- Gate de escrita permanece **NO-GO**. Relatório técnico: [GDM-C08](GDM-C08.md).

## GDM-C09 — Movimentação: estudo seguro sem escrita (15/10/2026)

- `PartitionMovePreview`: simulação de destino absoluto para partições GPT identificadas, somente para intervalos totalmente não alocados, sem sobreposição origem/destino; alinhamento de 1 MiB e seleção determinística de faixa.
- Novo painel WPF "Mover partição · prévia" com entrada de posição e botões "Sugerir destino" e "Analisar geometria"; resultados invalidados quando muda o alvo ou inventário.
- Introduzido `StorageOperationType.MovePartition` exclusivamente como representação de intenção de alto risco: o botão de fila a bloqueia, a política de segurança nega incondicionalmente, o planejador cria conflito e as camadas de execução rejeitam antes do PowerShell.
- **Não existe motor de cópia de dados, restauração ou escrita de partições em C09.** A conclusão técnica é da prévia e das proteções, não de movimentação real homologada.
- Testes sintéticos para limites, colisão, proteção e UI; gate global continua **NO-GO**. Ver [GDM-C09](GDM-C09.md).

## GDM-C10 — Análise GPT/MBR (15/10/2026)

- `DiskConversionAssessment`: verifica viabilidade geométrica e impeditivos sem fazer qualquer conversão; capacidade MBR depende de `2^32 × setor lógico`, com teto de 4 entradas primárias.
- Mensagens distinguem disco RAW vazio elegível a **planejamento de inicialização** de GPT/MBR com partições já existentes, que demandam motor de migração de dados não disponível.
- Cartão WPF **C10 · GPT / MBR** com consulta read-only para os dois destinos; invalida resultados antigos após seleção/refresh/falha; não gera `StorageOperation`.
- Testes sintéticos de setores 512/4096, boot, limites, identidade, RAW, tabelas GPT/MBR e UI; nenhuma conversão real.
- Gate de gravação **NO-GO**. Detalhes: [GDM-C10](GDM-C10.md).

## GDM-C11 — Clonagem: prévia não executável (15/10/2026)

- `ClonePreviewAssessment`: diagnóstico read-only dos pares origem/destino de discos inteiros ou de partições, sem inventar operações de escrita.
- Para **disco inteiro**, origem particionada GPT/MBR, destino RAW vazio, identidade forte/diferente e capacidade integral suficiente (não apenas espaço usado). Para **partição**, GPT básica, filesystem conhecido e faixa não alocada contígua alinhada a 1 MiB em outro disco GPT.
- Proteções: Disco 0, sistema/boot, EFI/MSR/Recovery, setores lógicos incompatíveis, ID duplicado, disco offline/destino somente leitura, espaço insuficiente, geometria sobreposta e incertezas de setor físico.
- Interface WPF: seleção explícita de destino, relatório de bloqueios/avisos; invalidação ao mudar origem/destino/inventário; nenhuma ação de fila ou executor adicionada.
- **Clonagem real e preservação de boot não implementadas**. Testes sintéticos de geometria/seleção; VHDX real e smoke WPF pendentes. Gate **NO-GO**. Detalhes: [GDM-C11](GDM-C11.md).

## GDM-C12 — Migração Windows: somente diagnóstico (15–16/10/2026)

- `WindowsMigrationAssessment`: estudo somente leitura de fonte/target com GUIDs GPT, Windows NTFS, ESP FAT32, MSR, WinRE, boot distribuído e capacidade integral.
- Disco 0 permitido **apenas como origem de inspeção**; destino requer outro disco, nunca Disco 0, RAW vazio, online, capacidade suficiente, setor lógico compatível e identidade forte.
- Checklist de evidência separa **Presente, Ausente e Não verificado**; BitLocker/TPM/BCD/UEFI/VSS/boot real permanecem desconhecidos. Windows Legacy/MBR sem protocolo homologado.
- Painel WPF com seletor de destino específico e relatório; dados invalidados em mudança de seleção, refresh ou falha; não enfileira nenhuma migração.
- `WindowsMigrationReport.CanExecute` fixo em `false`; motor real de migração e testes VHDX/boot não implementados. Gate global de escrita **NO-GO**. Relatório: [GDM-C12](GDM-C12.md).

## GDM-C13 — Saúde e diagnósticos (15/10/2026)

- Coleta read-only de temperatura, desgaste, horas e contadores de erros via `Get-StorageReliabilityCounter` somente para mídia associada por série única. Métricas ausentes ou inválidas não são apresentadas como zero.
- `StorageHealthAssessment`: evidência Windows com estados indeterminado/normal/atenção/crítico, alertas conservadores de volumes e distinção explícita de S.M.A.R.T. integral, que **não foi verificado**.
- Painel WPF e navegação de saúde, relatório efêmero em memória e invalidação com seleção, atualização e falha. Não existe ação de escrita, autoteste ou reparo.
- Testes sintéticos de saúde/parser/MVVM adicionados; execução e contagens dependem de CI no HEAD. **NO-GO** para gravação de disco físico e testes destrutivos.
- Relatório [GDM-C13](GDM-C13.md).

## GDM-C14 — Manutenção (15–16/10/2026)

- `MaintenanceAssessment` e `MaintenanceReport` oferecem matriz determinística **somente de metadados** para verificação de volume, otimização/TRIM/defrag, limpeza de temporários, espaço livre e apagar disco inteiro.
- Não lê diretórios pessoais, não executa ferramenta de reparo, não enfileira limpeza nem faz escrita. Não existe executor ou liberação de `CanExecute` para essas tarefas.
- Proteções de disco 0/sistema/EFI/MSR/Recovery, ausência de identidade, geometria inválida, volume read-only/offline e classificação SSD/HDD conservadora.
- UI WPF com seleção do tipo, risco, impedimentos e evidências; laudo invalidado em seleção/refresh/falha.
- Testes sintéticos e MVVM adicionados; resultado definitivo deve ser associado ao CI do commit de fechamento C14.
- Documentação: [GDM-C14](GDM-C14.md). Gate de escrita **NO-GO** e testes físicos/destrutivos pendentes.

## GDM-C15 — Comparação GPT de partições perdidas (15–16/10/2026)

- Comparação read-only de duas capturas do inventário Windows: referência GPT anterior à perda versus snapshot atualizado do mesmo dispositivo pelo `UniqueId` forte.
- Classifica entradas preservadas, geometria/tipo mudados, entradas desaparecidas com espaço livre, conflitos com novas partições e partições protegidas.
- Não varre bytes do disco, não procura assinaturas de filesystems e **não considera faixa não alocada prova de recuperação**.
- Referência apenas em RAM; comando requer disco inteiro, atualização posterior, geometria consistente. Descarta referência quando troca dispositivo ou há falha de inventário, invalida relatórios no refresh.
- `RecoveryReport.CanRecover=false` em todas as situações; **nenhum novo escritor/operador na fila**. Disco 0 somente leitura.
- Testes de avaliação e de ciclo de vida MVVM; o resultado verificável é o CI do HEAD. Ver [GDM-C15](GDM-C15.md). **NO-GO para recuperação executável/gravação física**.

## GDM-C16 — Acessibilidade e usabilidade WPF (16/10/2026)

- Mapa proporcional passou de `Border` com clique exclusivo de mouse para **`Button` focalizável**, com seleção Tab/Enter/Espaço, tooltip real e `AutomationProperties.Name/HelpText` contendo offset e tamanho.
- Segmentos observáveis (`IsSelected`) são mantidos na coleção ao selecionar/limpar partição, evitando perder foco por reconstrução desnecessária da árvore visual.
- Atalhos `Ctrl+1..8` (com numpad) para navegação por área; mapeamento isolado em função pura, sem capturar texto/Alt/Shift.
- Seções com âncoras de foco e nomes UI Automation; status apresenta texto completo em tooltip e `LiveSetting=Polite` — **comportamento do leitor de tela pendente de smoke real**.
- Testes MVVM e atalhos adicionados; contar os aprovados somente após CI deste HEAD. **Gravações permanecem bloqueadas**, e ainda faltam ensaios visuais/zoom/contraste/Narrator/NVDA. Relatório [GDM-C16](GDM-C16.md).

## GDM-C17 — Instalador, versão e verificação de atualizações (16/10/2026)

- **Sobre:** checagem manual de `releases/latest` via HTTPS restrito ao GitHub oficial, timeout, corpo limitado, sem redirect/credenciais. Compara `vX.Y.Z` estável e exige instalador x64 com nome, tamanho e URL oficial exatos.
- Nova versão permite abrir **somente a página oficial da release**; não realiza download, execução silenciosa ou autoatualização. Private/404/401/403, dados suspeitos e falhas de rede não são falsos positivos.
- **Inno Setup:** AppId preservado; campos de fabricante/site, comportamento de fechamento de aplicativos e preservação de dados fora de `{app}`. Nenhum dado em `%LOCALAPPDATA%` é removido pelos scripts.
- `Build-Installer.ps1` exige versão `X.Y.Z` e executável Windows com versão correspondente. CI gera e testa `SHA256SUMS.txt` para portable + instalador versionado; terceiro artifact contém checksums. Assinatura de código ainda **pendente**.
- Testes sintéticos do verificador de versão e validação de artefatos em GitHub Actions. Resultados devem ser confirmados no HEAD. Documentado em [GDM-C17](GDM-C17.md).
- **NO-GO** para publicar release comercial/auto-instalar/desbloquear gravações físicas antes de C18 e homologação de upgrade, uninstall e assinatura.

## GDM-C18 — Auditoria e decisão de comercialização (16/10/2026)

- **Resultado: NO-GO para comercialização, gravações físicas e auto-instalação.** Build ainda `0.1.0-dev`, `StorageWriteGate.ReleasedAfterSafetyAudit=false` e `GslV1TrustLoader.ApprovedPublicKeyFingerprintHex=""`.
- Matriz imutável de **12 requisitos** no domínio de release; teste garante que licença válida e evidências sintéticas não liberam os dois bloqueios compilados.
- Script `Test-CommercialReleaseGate.ps1` (não destrutivo) produz `commercial-readiness.json` com pendências; o modo `-RequireCommercialGo` falha explicitamente. CI registra e testa essa negação e publica o JSON como artefato.
- Assinatura Authenticode, instalação/upgrade/desinstalação Windows real, licença GSL real, NVDA/zoom, VHDX elevado, testes controlados de hardware e aprovação humana **ainda pendentes**.
- C18 **não** modifica `main`, não cria release/tag e não executa writes. Resultado completo: [GDM-C18](GDM-C18.md). CI verde não altera a decisão comercial.

## GDM-H01 — Pré-homologação Windows sem operações em discos (16/10/2026)

- Script `tools/Test-WindowsReadOnlyPreflight.ps1` verifica **somente arquivos dos artefatos** (nomes, SHA-256, versão e assinatura Authenticode) e sistema Windows x64. Não executa software/instalador nem consulta discos/partições.
- CI valida pacotes e prova recusa de manifesto adulterado em fixture separada; publica `GuiaSys-DiskManager-H01-readonly-preflight` com JSON de evidência **NO-GO**.
- Testes humanos Windows 10/11, UI em zoom variados, NVDA/Narrator, licença, instalação/upgrade/desinstalação **continuam pendentes**. Veja [GDM-H01](GDM-H01.md).
- `main` preservada, `StorageWriteGate` fechado, nenhum release comercial.

## GDM-H02 — Hardening GSL-v1 e pré-homologação (16/10/2026)

- Parser estrito com limite 8 KiB e verificação de chave pública ES256/P-256; rejeita campos duplicados, extras, JWK privado, caracteres incorretos, base64url não canônico. SHA-256 canônico deve corresponder a um **pin fixo embutido** e jamais é confiado somente por ter sido calculado localmente.
- `ApprovedPublicKeyFingerprintHex` **continua vazio intencionalmente**; `StorageWriteGate` continua falso.
- Unitários com chave EC efêmera sintética, sem rede; `tools/Test-GslV1TrustReadiness.ps1` gera `gsl-v1-readiness.json` e recusa `-RequireProductionTrust`, publicado como artefato H02 do GitHub Actions.
- Integração real requer public JWK + ID do projeto por origem oficial independente, novo build assinado e homologação humana trial/licença/revogação/offline. Nenhuma licença produtiva foi utilizada nesta fase.
- Consulte [GDM-H02](GDM-H02.md). **NO-GO comercial e NO-GO para gravações físicas.**

## GDM-H02.1 — Verificação offline de material público de assinatura (16/10/2026)

- Utilitário `tools/GslV1TrustInspector`: lê somente JSON público, exige `projectId` e `keyId` esperados, reutiliza parser JWK P-256 de H02 e exibe fingerprint SHA-256 **candidato**, com `decision: NO-GO`.
- CI: `--self-test` com chave pública efêmera e recusa de caminho para arquivo público ausente; nenhum acesso à API GSL ou ao Firebase e nenhuma configuração de produção.
- Antes de homologar: obter `projectId`, `keyId`, `x/y` públicos do painel oficial, revisar origem fora da banda e, só após assinatura e testes humanos, planejar pin/empacotamento. Confira [GDM-H02.1](GDM-H02.1.md).
- **NO-GO comercial e para gravações físicas permanece inalterado.**

## GDM-H02.1 — Atualização do contrato público GSL-v1 (16/10/2026)

- Domínio **oficial informado pelo administrador**: `https://licencas.guiasys.com.br/` (substitui `https://licencas.guiasys.online/`). O código de integração permanece o mesmo.
- Material de assinatura **público** do Disk Manager, no build de desenvolvimento: `projectId=prj_b41909541f924fe5b837`, `keyId=sig_3828131003da47bda2f7`, ES256/P-256 e arquivo minimalizado `gsl-v1-trust.json` copiado no publish. SHA-256 canônico pinado no binário; validação local com dados oficiais recebidos.
- Trial informado como desativado **no momento do contrato**, mas o servidor continua autoridade dinâmica. **Sem requisições de produção em H02.1.**
- Pré-verificação CI de conteúdo + pin + identidade e teste de alteração de chave. Mesmo com pin presente, o relatório comercial permanece `NO-GO`; faltam assinatura, homologação com licença real, Win10/11 e aprovação de escrita em mídia.
- Ver [GDM-H02.1](GDM-H02.1.md). Nada alterado em `main`, Firebase ou discos físicos.


## GDM-H03-UI — Reestruturação visual no Windows (16/10/2026)

- Barra superior azul com o logotipo GuiaSys, inventário, logs, licença e modo seguro, seguida de toolbar com ações rápidas e navegação.
- Conteúdo principal reordenado: tabela de partições → mapa proporcional + legenda → resumo → diagnósticos; painéis laterais mais compactos.
- Novo tema claro/azul com maior contraste na navegação. Estatísticas secundárias do mapa ficam recolhidas por padrão.
- Mantidas todas as ações e proteções MVVM, inclusive `ApplyCommand` indisponível por `StorageWriteGate=false`. Consulta ao GitHub Releases preservada; sem novo mecanismo de atualização automática.
- CI agora executa `tools/Test-DesktopUiContract.ps1`, valida o XAML e os bindings de leitura. Teste visual em Windows real/DPI é **pendente**; liberação comercial continua **NO-GO**.
- Registro: [GDM-H03-UI](GDM-H03-UI.md).
