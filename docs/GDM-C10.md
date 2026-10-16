# GDM-C10 — Análise de conversão GPT / MBR (sem escrita)

**Data:** 15/10/2026 · **Repositório:** `guiasysstudio/GuiaSys-DiskManager` · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C09

## Decisão técnica e escopo

O GDM-C10 implementa um **diagnóstico de compatibilidade geométrica e de riscos da conversão de discos GPT ↔ MBR**, sem modificar armazenamento, migrar metadados ou copiar volumes. Não é uma funcionalidade de conversão sem perda de dados, porque tal promessa exigiria motor especializado e homologado. O Windows `Initialize-Disk` só é apropriado para **discos RAW vazios** e **não** deve ser usado como conversão de discos contendo dados. `MBR2GPT.EXE` tem requisitos muito específicos para instalações Windows e não é uma API universal de conversão de discos de dados; **não é chamado**.

## Implementação

`Operations/DiskConversionAssessment.cs` recebe um snapshot somente leitura, o disco e o esquema de destino (`Gpt` ou `Mbr`). Produz `DiskConversionReport` contendo esquema atual, desejado, setor lógico, partições, impedimentos, avisos e distinção expressa entre **compatibilidade geométrica** e **conversão preservando dados**. A propriedade `DataPreservingConversionSupported` sempre retorna `false`. A avaliação não é uma autorização de operação.

- Reconhece apenas os esquemas RAW/GPT/MBR reportados pelo Windows e recusa esquemas desconhecidos, discos removidos e inventários com geometria inconsistente.
- Recusa Disco 0, ausência de identidade forte, unidades offline/somente leitura, discos de sistema/inicialização e discos com partições protegidas (incluindo EFI/MSR/Recovery/ocultas/C:).
- Requer setor lógico válido (potência de dois de 512 a 65.536 bytes) e capacidade alinhada.
- Para destino **MBR**, calcula endereçamento clássico LBA de 32 bits usando o **tamanho real do setor lógico** (não supõe sempre 512 B): limite de endereçamento = `(2^32) × tamanho_do_setor`, recusando disco que exceda esse tamanho. Trata esse teste como representabilidade, não autorização de conversão. Recusa mais de 4 entradas primárias e extents existentes desalinhados; alerta que GUIDs/tipos GPT não se transferem literalmente para MBR.
- Para destino **GPT**, alerta sobre firmware UEFI/Legacy, boot, tipos de partição estendidos/MBR e necessidade de motor especializado.
- Sempre presume que uma partição existente pode conter dados, mesmo que o filesystem ou contadores de volume estejam ausentes. A análise **nunca** oferece gravação ou conversão segura sem backup.
- Para **RAW sem partições**, a análise pode informar **candidato a inicialização** em GPT/MBR, que é diferente de converter discos já particionados. A implementação anterior permite planejar `InitializeGpt`/`InitializeMbr` apenas para RAW vazio, e o bloqueio global de gravações permanece inalterado.
- Para discos que já possuem o esquema escolhido, identifica não-operação, evitando instruções enganosas.

## Interface WPF

Foi incluído um cartão **C10 · GPT / MBR** no painel de operações, com botões **Analisar → GPT** e **Analisar → MBR**. Requer seleção do **disco inteiro** (nenhuma partição selecionada). O resumo apresenta os impedimentos e avisos, sempre com indicação explícita **CONVERSÃO COM DADOS NÃO IMPLEMENTADA**. A análise é invalidada após atualização do inventário, falha de leitura ou troca de alvo; resultados antigos não permanecem como se fossem atuais.

A análise não cria `StorageOperation`, não entra na fila, não dispara PowerShell, não solicita credenciais nem chama `MBR2GPT` ou conversores externos.

## Testes e validação

`tests/GuiaSys.DiskManager.Tests/DiskConversionAssessmentTests.cs` cobre RAW vazio vs disco particionado; fronteira LBA MBR para setores 512 B/4096 B; número de entradas MBR; alinhamento; partições do sistema; Disco 0; identidade insuficiente; disco somente leitura/offline; inventário inválido; conversão para o esquema já presente; limitações de boot e recusa das operações antigas `Initialize*` em discos particionados.

`tests/GuiaSys.DiskManager.Tests/MainViewModelUiTests.cs` verifica habilitação dos botões de análise sem partição selecionada, resumos, distinção de inicialização RAW, ausência de fila, bloqueio de aplicação e invalidação de dados ao alterar o alvo ou falhar o inventário.

CI Windows: verificação não executante da sintaxe dos scripts PowerShell, compilação Debug/Release, xUnit, publicação win-x64 self-contained, instalador Inno Setup e artefatos. **Os testes automatizados não substituem testes de campo.**

## Limites e próximos passos

1. **Nenhuma conversão com preservação de dados foi implementada ou homologada.** Compatibilidade geométrica é insuficiente para garantir segurança dos sistemas de arquivos, boot e tipos de partição.
2. Não existe mecanismo universal testado de GPT ↔ MBR para discos em uso. Não serão usados atalhos destrutivos (`Clear-Disk`, `clean`, exclusão/recriação) para simular conversão sem perda.
3. Homologação de laboratório em VHDX descartáveis, proteção de volumes montados, recovery, journal de retomada, testes com setores físicos/lógicos distintos, firmware e licenciamento continuam pendentes.
4. O gate `StorageWriteGate.ReleasedAfterSafetyAudit` permanece **false**. Nenhuma operação de gravação em discos físicos ou virtuais foi executada neste módulo.
5. Próxima etapa planejada: **GDM-C11 — Clonagem de discos e partições**, inicialmente com inventário de requisitos e verificações, sem liberar mutações.

**Decisão de produção:** **NO-GO para conversão real**, com análise visual read-only concluída.
