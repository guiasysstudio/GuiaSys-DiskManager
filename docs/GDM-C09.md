# GDM-C09 — Movimentação de partições: análise geométrica (sem escrita)

**Data:** 15/10/2026 · **Repositório:** `guiasysstudio/GuiaSys-DiskManager` · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C08.

## Escopo entregue

Módulo C09 desenvolve uma **prévia geométrica de reposicionamento de partições** dentro do **mesmo disco GPT**, com verificação rigorosa de extents, alinhamento e identidade. **Não implementa cópia de blocos, movimentação da tabela GPT, alteração de offsets, journal de recuperação de dados ou operação física de mover partição**. Assim, não seria correto apresentá-lo como movimentação executável ou pronta para produção.

O Windows PowerShell Storage não oferece um cmdlet equivalente a mover uma partição preenchida preservando seus bytes e metadados. Não substituí essa ausência por excluir e recriar partições, alterar GUID manualmente, confiar em ferramentas não homologadas ou executar comandos destrutivos.

## Componente de prévia

`Operations/PartitionMovePreview.cs`:

- Usa `StorageGeometry.Build` com intervalos de bytes semiabertos `[início, fim)`.
- Exige disco GPT identificado, exceto Disco 0, partição com identidade GPT forte, setor lógico informado, layout completo e ausência de partições protegidas no disco.
- Recusa discos offline/somente leitura, partições offline/somente leitura, Windows/boot/EFI/MSR/Recovery e outros tipos GPT protegidos.
- Recebe **destino como deslocamento absoluto em bytes** no mesmo disco; não confunde com tamanho final nem número de setores.
- Usa grade conservadora de **1 MiB** para início do destino, compatível com setor lógico de 512 a 65.536 bytes em potências de dois. Essa regra é uma política de estudo, **não um alinhamento certificado para todos os dispositivos**.
- Exige que todo o intervalo do destino caiba em **uma única faixa já não alocada**. Recusa colisão com outra partição e sobreposição entre origem e destino; não há mecanismo de cópia in-place homologado.
- Calcula origem, destino, direção, tamanho de dados a preservar e extensão livre correspondente; **nenhum byte é copiado**.
- `SuggestDestination` escolhe a posição mais próxima entre faixas inteiras elegíveis e alinhadas, de modo determinístico. Uma sugestão de prévia **não autoriza gravar**.
- Rejeita qualquer geometria inválida, intervalos fora do disco, origem ausente, GUID indefinido ou falta de informação de setor.

## Interface WPF

Novo item **Mover partição · prévia** na lista e nas ações contextuais. O painel de análise apresenta:

1. Deslocamento absoluto de destino (bytes, MB ou GB).
2. **Sugerir destino**, que sugere e analisa a faixa não alocada segura mais próxima.
3. **Analisar geometria**, que valida o destino informado e explica eventuais rejeições.
4. Aviso permanente: **SEM EXECUÇÃO** — não há motor homologado de cópia e recuperação.

O botão de inclusão na fila fica desabilitado para movimentação. O `MainViewModel.QueueSelectedOperation` também recusa explicitamente qualquer tentativa de inclusão por chamada de código, independentemente do botão. Depois de atualização de inventário, troca de seleção ou falha de leitura, a prévia anterior é invalidada e não é apresentada como atual.

## Barreiras independentes de execução

A representação `StorageOperationType.MovePartition` e `TargetOffsetBytes` permite testar tentativas de uso indevido. O contrato (`EssentialOperationValidator`) verifica formato de alvo e deslocamento, mas isso **não significa autorização de execução**.

- `SafetyService.Evaluate` retorna **negação incondicional** para movimentação, independentemente da viabilidade geométrica.
- `OperationPlanner.Plan` classifica esse tipo como conflito e não projeta um novo inventário pretendendo que uma cópia tenha ocorrido.
- `MainViewModel.QueueCommand` não aceita `MovePartition`.
- `PowerShellStorageOperationExecutor.ExecuteAsync` recusa esse tipo explicitamente, sem invocar PowerShell.
- `SafeExecutionCoordinator.ExecuteAsync` possui uma segunda recusa explícita de movimentação, antes de licenciamento, journal, gate ou delegado de execução.
- O script PowerShell mantém a allowlist de operações previamente suportadas e seu ramo `default` recusa outros tipos.
- `StorageWriteGate.ReleasedAfterSafetyAudit` permanece **false** para todos os tipos.

## Validação

`PartitionMovePreviewTests.cs` cobre: prévia em área vazia, áreas menores que a partição, cálculo de destino sugerido, igualdade de posição, colisão e sobreposição, limites do disco, alinhamento, identidade GPT, setores ausentes, disco 0 e partição de recuperação, recusa do planejador, invalidação de destino em outra operação e gate de gravação permanentemente fechado.

`MainViewModelUiTests.cs` cobre: prévia via botões, impossibilidade de enfileiramento, invalidação após atualização/troca de seleção e falta de execução pelo usuário.

O CI Windows inclui parser AST **não executante** dos scripts PowerShell, build Debug/Release, xUnit, publish self-contained win-x64 e Inno Setup. A aprovação do CI não equivale a testes de cópia real de dados.

## Requisitos para uma futura movimentação executável

Antes de considerar uma implementação real de movimentação são necessários, no mínimo, um motor de movimentação/cópia de blocos revisado, tratamento de leitura/escrita com exclusão de acesso ao volume, validação de setores, preservação de metadados GPT e NTFS/exFAT/etc., operações a frio para volumes montados, plano de restauração, journal durável com fases recuperáveis, hashes de origem/destino, isolamento entre processos, recuperação após falha energética e homologação destrutiva em VHDX descartáveis sob Windows. A segurança dos volumes do sistema e do Disco 0 continua exigindo proteção independente.

**Conclusão:** C09 entrega **análise e protótipo funcional de prévia**, não suporte físico de movimentação. **NO-GO de escrita mantido.** O próximo C10 poderá avançar em conversão/inspeção de esquemas sem apagar dados, sempre com os mesmos bloqueios.
