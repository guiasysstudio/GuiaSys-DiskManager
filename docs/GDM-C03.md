# GDM-C03 — Segurança, identidade e confirmação de alvos

**Data:** 15/10/2026. **Branch:** `feat/full-disk-manager`. **Base:** GDM-C02. Não houve merge para `main`.

## Objetivo

Reduzir os bloqueios P0 levantados na auditoria GDM-C00, especialmente a identidade de partição ausente, a chamada pública ao executor fora da interface, a janela de troca de alvo e a confirmação que considerava só o primeiro disco.

## Alterações entregues

### Identidade de partição e inventário

- O inventário Windows consulta `PartitionGuid` e `GptType` (Microsoft `Get-Partition`).
- `PartitionIdentity.TryGetFingerprint` produz uma identidade canônica de topologia ligada a GUID GPT não zerado, número do disco, número da partição, offset, tamanho e tipo. O fingerprint é parte de `StorageOperation.ExpectedPartitionIdentity`, capturado no momento da seleção.
- `SafetyService` recusa operações destinadas a partições sem GUID forte (por exemplo, partições MBR), fingerprint ausente, partição substituída, renumerada ou movida/redimensionada. Essa restrição é conservadora e **não representa suporte comercial a MBR nesta etapa**.
- GUIDs malformados e repetidos no mesmo disco são recusados no parsing de inventário. Disco ainda exige identidade forte `UniqueId` e numeração consistente.
- O fingerprint exclui rótulo e letra, para não invalidar mudanças previsíveis de metadados. Flags de proteção são verificadas no snapshot real, não aceitas do momento de enfileiramento.

### Regras de proteção

- `SafetyService` recusa layout em disco que contenha partição protegida, incluindo `CreatePartition`, `DeletePartition` e `ResizePartition`.
- Reconhecimento de boot/sistema, oculta, EFI, MSR, Recovery, unidade C: e caminhos de montagem sob C:\\, além de `GptType` não reconhecido como Microsoft Basic Data. Tipo GPT desconhecido é conservadoramente protegido.
- O uso de `Disk 0` continua proibido para qualquer mutação. Identidades ausentes ou ambíguas falham fechadas.

### Fronteira de execução

- `PowerShellStorageOperationExecutor.ExecuteAsync` agora exige internamente o gate de autorização, serviços de inventário, segurança e licença, privilégios administrativos, licença válida, um novo snapshot e avaliação completa da política de alvo. Isso bloqueia também chamadas diretas sem WPF.
- A aplicação injeta essas dependências no executor. O construtor legado apenas com logger não autoriza gravação.
- O script PowerShell de execução compara de novo GUID GPT, número, offset, tamanho e tipo, bem como `UniqueId` do disco, imediatamente antes de selecionar a operação. Também verifica proteções de layout e flags da partição.
- **O gate `StorageWriteGate.ReleasedAfterSafetyAudit` continua retornando `false`.** Nenhuma chamada pode passar desse gate e atingir cmdlets mutadores na versão C03. As políticas de execução são preparatórias para os gates C04, não aprovação para gravar.

### Confirmação multi-disco

- `OperationConfirmation` cria um resumo para **todos** os discos e partições envolvidos, independentemente da ordem da fila.
- Confirmação reforçada exige digitar `DISCO N` ou `DISCOS N,M`, com os números ordenados. Confirmar somente o primeiro disco não autoriza o conjunto.
- `ApplyAsync(true)` não contorna a necessidade de confirmação. A rotina exige o callback de confirmação explícita.

## Testes e limites de garantia

Cobertura de: GUID forte, GUID ausente/zerado, renumeração, troca de GUID, deslocamento, mudança de tamanho ou tipo, tipos GPT protegidos, caminhos C:, duplicação de GUID, múltiplos discos e tentativa de chamada direta ao executor com gate fechado.

**Nenhuma gravação foi liberada em disco físico.** Testes automatizados usam modelos, dependências falsas e execução bloqueada. O runner VHDX legado que chamava o executor diretamente permanece sem permissão de gravação até adaptação à autorização revisada. Não declarar VHDX de escrita testado nesta etapa.

## Pendências para o GDM-C04

- Journal estruturado persistente e estados de execução/cancelamento.
- Verificação externa e rigorosa de pós-condições e resultado indeterminado/parcial.
- Isolamento/serialização por alvo; mitigação restante de TOCTOU além da nova reconsulta.
- Regra exata de bloqueio de volumes montados/em uso, capacidade de filesystem e validação Windows de alinhamento.
- Redesenho e revalidação do runner VHDX, inclusive tratamento de erros/cleanup; ensaios exclusivamente em VHDX descartável e sob autorização explícita.
- Critérios formais de liberação do `StorageWriteGate`. **Não liberar gravações como efeito colateral da conclusão de C03.**

## Decisão

Núcleo de identidade, reforço do executor e confirmação multi-alvo implementados, sujeitos aos testes do HEAD final. **Gravação comercial: NO-GO até GDM-C04 e auditoria específica.**
