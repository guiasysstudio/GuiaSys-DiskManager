# GDM-C06 — Inventário e visualização avançada

**Data:** 15/10/2026. **Repositório:** `guiasysstudio/GuiaSys-DiskManager`. **Branch:** `feat/full-disk-manager`. **Base:** GDM-C05.

## Escopo

Aprimoramento **estritamente de leitura** da captura de propriedades dos discos, classificação, cálculo de ocupação e apresentação no painel WPF. O inventário não executa comandos de mutação. A fila, o planejador, a proteção de partições e o licenciamento GSL-v1 permanecem como nos módulos anteriores.

## Inventário Windows

- `PowerShellDiskInventoryService` coleta propriedades opcionais `MSFT_Disk.LogicalSectorSize` e `PhysicalSectorSize`; valores ausentes aparecem como **Não informado**.
- Dados opcionais de volume vêm de `Get-Volume.Size` e `SizeRemaining`, *somente quando os contadores são consistentes*. Se o volume não tem métricas, a UI mostra **Não disponível**, não um valor falso de 0 bytes livres.
- Um volume realmente cheio, com capacidade conhecida e `SizeRemaining=0`, mostra corretamente **0 bytes livres e 100% utilizado**.
- `Get-PhysicalDisk` é associado a `Get-Disk` **somente quando há correspondência única de número de série informado por ambos**. O antigo pareamento por `PhysicalDisk.DeviceId = Disk.Number` foi removido porque esses identificadores pertencem a namespaces distintos e poderiam confundir discos. Sem correspondência inequívoca, o tipo de mídia e a temperatura permanecem indisponíveis.
- A tecnologia é identificada como SSD/HDD somente quando a API de armazenamento informa expressamente o tipo de mídia. Barramento NVMe por si só é apresentado como **protocolo NVMe**, não como prova de mídia SSD.
- O parser isolado `PowerShellDiskInventoryService.ParseInventory` valida a mesma geometria usada pelo executor e planejador. Novos campos são opcionais; máquinas Windows sem esses campos continuam compatíveis com inventário de leitura.

## Projeções e métricas

- `InventoryInsights.Build` constrói métricas imutáveis a partir de `StorageGeometry.Build`. Por disco: capacidade total, bytes particionados, bytes não alocados, maior faixa contígua não alocada, quantidade de partições e proporções.
- O resumo geral consolida capacidade de discos, espaço não alocado, discos sem identidade forte e número de volumes cujas métricas do Windows estão disponíveis.
- A soma entre discos usa controle de overflow; se exceder `Int64`, a UI exibe **Capacidade acima do limite de exibição** e não inventa um total.
- A ocupação do filesystem usa **somente** `VolumeCapacityBytes - VolumeAvailableBytes`. Não confundir espaço **não alocado no disco** com espaço **livre dentro da partição**: uma partição pode estar completamente cheia e o disco ainda possuir área não alocada.
- Para volumes com métricas válidas: porcentagem de uso e aviso de capacidade reduzida a partir de 85%, e crítico a partir de 95%. São alertas de **capacidade**, não prognóstico de saúde do disco.

## Interface WPF

- Cards de capacidade agregada, não alocado, quantidade de discos/partições e volumes monitorados.
- Barra de capacidade particionada para o disco selecionado, indicando separadamente área particionada, não alocada e maior faixa livre contígua.
- Mapa de partições com largura mínima de 12 px para acessibilidade; a escala é explicitamente **aproximada** para partições pequenas, com tamanho real no tooltip e na tabela.
- Sidebar detalha conexão e tecnologia, sem deduzir SSD apenas pelo protocolo. Painel de disco mostra conexão, estado, saúde relatada pelo Windows, temperatura e tamanhos de setor lógico/físico quando presentes.
- Tabela apresenta espaço livre e percentual usado **no volume**, com a distinção visível. Painel de detalhes exibe capacidade de volume, utilização, classificação GPT, proteção e caminhos.
- `PartitionClassifier` considera GUID de tipo GPT para classificar EFI/MSR/Recovery **antes** do filesystem, impedindo que uma partição Recovery com NTFS apareça como partição de dados comum.
- Após nova leitura, os agregados, seleção e detalhes são sincronizados; erros de inventário limpam projeções visuais obsoletas.

## Testes

`InventoryInsightsTests.cs` cobre cálculo de espaço não alocado vs livre dentro do volume, discos vazios, agregação multi-disco, overflow, volume realmente cheio, volume sem métricas, estatísticas inválidas, limites de aviso, inferência conservadora do tipo de mídia, tipos GUID GPT, tamanhos de setor e parsing de JSON Windows com campos opcionais presentes/ausentes/malformados.

`MainViewModelUiTests.cs` testa binding dos novos resumos ao atualizar o inventário e limpeza de métricas após falha. A validação do **HEAD final** deve ser confirmada em GitHub Actions: restore, compilação Debug e Release, xUnit, publicação win-x64 e instalador Inno Setup.

## Limites e próximas verificações

- Os indicadores de saúde e temperatura são informações **relatadas pelo subsistema de armazenamento do Windows**, não diagnóstico SMART profundo nem previsão de falha. Dispositivos sem contadores aparecem como indisponíveis.
- A UI/compilação não substitui smoke interativo em Windows 10/11, testes com USB/SATA/NVMe/RAID e verificação visual em diferentes resoluções. **Esses testes manuais ainda não foram realizados.**
- Não existe monitoramento automático em segundo plano: atualização manual por botão ou F5. A consulta está limitada ao snapshot capturado naquele momento.
- Operações que modificam discos continuam **NO-GO**. `StorageWriteGate.ReleasedAfterSafetyAudit` permanece `false`; não foram realizados testes destrutivos em VHDX ou discos físicos.
- Próximo módulo planejado: **GDM-C07 — Operações Essenciais**, com implementação preparatória, validação e gates de segurança, **sem ativação automática de escrita**.
