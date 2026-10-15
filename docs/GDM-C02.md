# GDM-C02 — Núcleo de gerenciamento de discos, partições e volumes

**Data:** 15/10/2026. **Branch:** `feat/full-disk-manager`. **Base:** `751291ad295ba48dbe05d855873d3ed3ab09733b` (GDM-C01). A branch `main` não recebe alterações nesta etapa.

## Objetivo e decisões de implementação

Criar o núcleo **não destrutivo** para modelar geometria, simular estados conhecidos e identificar conflitos e dependências antes de executar qualquer operação. Preservar inventário, fila, WPF/MVVM, executor legado, licença GSL-v1 e CI.

### Camada de domínio e inventário

- `StorageGeometry.Build(StorageSnapshot)` gera `StorageLayout` e `DiskLayout` para todos os discos. As extensões `StorageExtent` são intervalos semiabertos `[Offset, EndExclusive)` com comprimento positivo e identificador opcional de partição.
- Cada lacuna até o próximo início e após a última partição vira um intervalo não alocado. O maior intervalo não alocado é distinto da soma de espaços livres, impedindo que a UI mostre espaço fragmentado como totalmente contíguo.
- Rejeita discos ou partições duplicados, partições órfãs, tamanhos nulos/negativos, limites numéricos, sobreposições e partições além do disco; erros são `InvalidDataException`. O inventário PowerShell usa esse mesmo validador.
- `BuildPartitionMap` usa as extensões reais do domínio, não repete o algoritmo de cursor no ViewModel.

### Camada de aplicação

- `IOperationPlanner.Plan(snapshot, operations)` não possui referência a executor/PowerShell; é determinístico e não altera o snapshot de entrada.
- Valida identidade do disco e aplica `ISafetyService` a um snapshot projetado a cada etapa. Mudanças previsíveis de estado de disco e metadados de partição são projetadas; erros impedem incluir novos itens.
- Proíbe conflito de letra na visão global de discos, inclusive conflito causado por uma etapa anterior da fila.
- Para `CreatePartition`, compara a dimensão solicitada com a **maior** extensão livre, não com a soma. Para expandir, exige espaço contíguo imediatamente após a partição; reduzir não presume validade dos clusters/FS (checar com Windows na execução).
- `InitializeGpt`, `InitializeMbr`, `CreatePartition`, `DeletePartition` e `ResizePartition` estabelecem barreira de geometria para etapas posteriores do mesmo disco; `FormatPartition` estabelece barreira da partição. Não se inventam novos números de partição nem offsets futuros.
- O resultado `OperationPlan` contém estado por etapa, primeira colisão, conjunto de discos afetados e snapshot projetado, para futura confirmação/revalidação.

### Integração WPF/MVVM

- Antes do `Enqueue`, recalcula o plano incluindo o candidato. Operações inválidas são recusadas com diagnóstico.
- Desfazer, limpar e atualizar o inventário recalculam o resumo do plano. O cabeçalho da fila e o detalhe do disco exibem informações de geometria e revisão.
- Antes de `ApplyAsync`, faz pré-validação do plano, mas `StorageWriteGate.ReleasedAfterSafetyAudit` permanece **false**.
- Nenhum uso de `System.Management`, drivers de terceiros, GParted, MiniTool ou bibliotecas copyleft foi incorporado.

## Limites e riscos intencionalmente ainda bloqueados

1. A prévia **não é um plano de execução autoritativo**: Windows decide alinhamento, espaço reservado GPT/MBR, capacidade do filesystem, montagem/lock de volumes.
2. Falta `ExpectedPartitionIdentity`/fingerprint imutável e revalidação da partição imediatamente antes da escrita no executor legado (**GDM-C03**).
3. O executor PowerShell atual ainda não força por conta própria todas as políticas de segurança. **Não habilitar escrita física** enquanto isso estiver pendente (**GDM-C03**).
4. Confirmar múltiplos discos com `operations[0].DiskNumber` permanece problema conhecido (**GDM-C03/C04**).
5. Journaling, pós-condições, idempotência e tratamento de cleanup VHDX continuam para **GDM-C04**. Não foi executado teste destrutivo em discos físicos.
6. Janela de ativação real GSL-v1 ainda depende do fingerprint público de confiança do produto, conforme GDM-C01.

## Testes e verificação

- Testes xUnit em `StoragePlanningTests.cs`: intervalos meio-abertos, espaço contíguo, discos vazios, colisões e órfãos, preservação de snapshot, mudanças encadeadas, conflitos cross-disk, barreiras de layout e formatação, flags, disco RAW e dimensionamento.
- Execução de teste com APIs de armazenamento **simuladas**. Sem disco físico, sem UAC, sem VHDX mutável em C02.
- Gate de compilação, testes Debug/Release, publish Windows x64, instalador Inno Setup e upload de artefatos evidenciado no GitHub Actions; não considerar CI de outros SHAs como aprovação deste HEAD.

**Decisão:** integração de domínio e pré-planejamento concluídos no código; autorização de gravação comercial: **NO-GO** até as verificações obrigatórias posteriores.
