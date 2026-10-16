# GDM-C12 — Migração do Windows: diagnóstico de topologia e boot (somente leitura)

**Data:** 15–16/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C11.

## Entrega e limite explícito

O C12 implementa **análise de metadados** da preparação para migrar uma instalação Windows de um disco a outro. Não implementa migração, clonagem do sistema, reconfiguração de boot, edição do BCD, alteração de GPT, transferência de arquivos, snapshot VSS, suspensão BitLocker ou restauração de Windows/WinRE.

**`WindowsMigrationReport.CanExecute` é sempre `false`**. Nenhum novo tipo de operação de migração foi incluído na fila nem na allowlist PowerShell. O bloqueio global `StorageWriteGate.ReleasedAfterSafetyAudit => false` continua em vigor.

## Fonte e destino

- **Origem:** um disco com instalação Windows identificada por inventário `IsBoot` e partições do sistema. Ao contrário das operações de escrita e da prévia de clonagem comum, o **Disco 0 pode ser inspecionado somente para leitura**, porque é frequentemente a fonte do Windows em execução. Origem offline ou sem identidade confiável é classificada como inadequada.
- **Destino:** outro dispositivo com `UniqueId` forte e diferente, **não Disco 0**, online, não somente leitura, sem flags de boot/sistema, **RAW e sem qualquer partição**. Não oferece sobrescrever dados existentes.
- **Capacidade:** exige espaço igual ou superior à **capacidade física inteira da origem**, não à quantidade de bytes usados no NTFS. Não é implementada a migração automática para SSD menor ou rearranjo de partições.
- **Setores lógicos:** tamanhos 512–65.536 bytes em potência de dois, compatíveis entre fonte e destino, e capacidades alinhadas. Incerteza ou diferença nos setores físicos gera aviso; a análise não certifica compatibilidade física.
- **Geometria:** aplica `StorageGeometry.Build` para detectar GUIDs duplicados, discos ambíguos, partições sobrepostas, extents inválidos e ausência de discos.

## Requisitos mínimos de topologia de boot GPT/UEFI

O avaliador `WindowsMigrationAssessment` consulta **apenas o inventário existente**; não executa `bcdboot`, `mbr2gpt`, `reagentc`, `manage-bde` ou comandos de gravação. Para marcar **topologia preliminarmente plausível**, exige:

1. **Partição Windows:** exatamente uma com `IsBoot` e `NTFS`, acessível, com tipo GPT de dados básicos.
2. **ESP/EFI:** exatamente uma partição com GUID de tipo EFI, conhecida como `FAT32`, com tamanho válido.
3. **MSR:** exatamente uma partição Microsoft Reserved, de tamanho válido.
4. **Partições GPT:** identificadores únicos fortes e GUIDs de tipo verificáveis em todas as partições da origem.
5. **Cadeia de boot:** caso haja partição marcada `IsSystem` em **outro disco**, o diagnóstico recusa presumir que todo o Windows esteja no dispositivo escolhido.

**Recovery/WinRE:** presença é indicada, mas não comprova vínculo real com `WinRE`; ausência/multiplicidade gera aviso, não uma falsa garantia. Em origem **Legacy BIOS/MBR**, o diagnóstico apresenta impedimento explícito: o inventário não contém provas suficientes de partição Active, boot manager e BCD; um fluxo próprio precisaria ser desenvolvido.

## Evidências NÃO verificadas

O relatório apresenta os itens como `Present`, `Missing` ou `Unknown`. Os campos `BitLocker/criptografia`, `BCD/firmware UEFI/Secure Boot` e `consistência dos arquivos/VSS` permanecem **Unknown**, mesmo que a topologia seja plausível. Não consulta chaves de recuperação, firmware, TPM, registro de BCD, configuração de WinRE, partições dinâmicas, subsistemas especiais, dependências de aplicações, credenciais ou integridade real dos dados.

O diagnóstico **não é uma autorização** para executar uma migração, não comprova que o destino inicializará e não pode ser confundido com backup.

## Interface WPF

Novo cartão **C12 · Migrar Windows (diagnóstico)** com:
- Fonte = disco inteiro atualmente selecionado, permitindo inspecionar o Disco 0 sem escrita.
- Destino = seleção independente `SelectedMigrationTargetDisk`, sem seleção automática de unidade potencialmente destrutiva.
- Botão **Verificar requisitos do Windows**, habilitado apenas com fonte e destino escolhidos e sem partição da origem selecionada.
- Checklist visual por componente, com estado claro `Presente`, `Ausente/ambíguo` ou `Não verificado`, seguido de impedimentos e alertas.
- Invalidação do relatório após troca de origem, partição, destino, atualização de inventário ou falha. A seleção do destino é restaurada somente se a **mesma identidade de dispositivo** ainda existir; nunca troca silenciosamente o alvo removido por outro disco.

## Testes e homologação

`WindowsMigrationAssessmentTests.cs` usa inventários GPT/UEFI inteiramente sintéticos: Disco 0 como fonte read-only, partição Windows `IsBoot`, EFI FAT32, MSR, WinRE ausente, BCD/BitLocker/VSS desconhecidos, boot distribuído por discos, modo Legacy não suportado, destino menor/ocupado, origem/destino iguais, setor inválido e geometria sobreposta.

`MainViewModelUiTests.cs` cobre seleção manual da origem/destino, inspeção do Windows no Disco 0 sem operações enfileiradas, invalidação com refresh e troca de alvo, destino removido e falha de inventário.

O GitHub Actions em Windows executa parse AST não executante de scripts PowerShell, compilação Debug/Release, testes xUnit, publicação win-x64 e instalador Inno Setup. **Não executou migrações nem escritas em VHDX ou discos físicos**.

## Pendências e decisão

Para uma migração **real**, ainda seria necessário desenvolver e auditar: cópia offline/VSS confiável, proteção contra seleção invertida da unidade, gerenciamento de BitLocker e chaves, escrita dos metadados GPT e GUID sem colisão, ESP/BCD/WinRE, migração de boot/UEFI NVRAM, drivers, recuperação pós-falha, journaling durável por etapas, integridade criptográfica, isolamento entre processos e testes de inicialização real em VM descartável com VHDX.

**Aceite C12:** funcionalidade de **diagnóstico e checklist de topologia** implementada. **NO-GO** para migração real ou para qualquer gravação em armazenamento. **Próximo: GDM-C13 — Saúde e diagnósticos de discos.**
