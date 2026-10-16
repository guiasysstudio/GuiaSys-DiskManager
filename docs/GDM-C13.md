# GDM-C13 — Saúde e diagnósticos: inventário Windows sem gravações

**Data:** 15/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C12.

## Objetivo e fronteiras

Exibir evidências de saúde fornecidas por APIs nativas de armazenamento do Windows **em modo somente leitura**, com procedência, indisponibilidade explícita e alertas conservadores. Nenhum `CHKDSK`, `Repair-Volume`, `smartctl`, autoteste SMART, varredura de superfície, mudança de política do dispositivo, benchmark, escrita ou reparo foi adicionado.

**Não é leitor SMART integral**: `Get-StorageReliabilityCounter` oferece alguns contadores expostos pelo controlador e não substitui atributos brutos ATA SMART, log NVMe, dados proprietários, autotestes ou diagnóstico do fabricante. Resultados não podem ser interpretados como certificação de vida útil ou previsão confiável de falha.

## Coleta nativa

- Base `Get-Disk`: `HealthStatus`, `OperationalStatus`, offline/read-only, setores e identificação existente.
- `Get-Partition | Get-Volume`: `HealthStatus` e espaço disponível quando informado pelo Windows. Ocupação alta constitui **alerta de capacidade**, não prova de dano ao dispositivo.
- `Get-PhysicalDisk | Get-StorageReliabilityCounter`: temperatura, desgaste (percentual reportado), horas de uso e totais de erros de leitura/gravação, inclusive não corrigidos.
- A associação entre `MSFT_Disk` e `MSFT_PhysicalDisk` **permanece restrita a número de série único dos dois lados**. Não associa `DeviceId` com `Get-Disk.Number` por coincidência numérica.
- Falha de consulta, campo ausente, percentual fora de [0,100], contador negativo/maior que `Int64.MaxValue` e temperatura fora da faixa de leitura são tratados como **desconhecidos**. Zero é legítimo somente se efetivamente reportado.
- Campos de telemetria são opcionais; inventários legados sem eles continuam compatíveis.

## Análise e interface

`StorageHealthAssessment` transforma somente o `StorageSnapshot` em `DiskHealthReport` com `Unknown`, `Normal`, `Warning` e `Critical` (não deve ser confundido com certificado SMART). Alertas de `Unhealthy` vindos do Windows são críticos; `Warning`, offline, erros históricos não corrigidos, temperatura >= 65 °C e desgaste >= 95% são apenas sinais conservadores para investigação. Esses dois limiares são **regras informativas do aplicativo**, não especificações universais nem limiares do fabricante.

O painel central **C13 · Saúde e diagnósticos** permite selecionar o disco (inclusive Disco 0, sem escrita) e **Analisar indicadores**. O comando atua somente sobre o snapshot em memória; **F5 / Atualizar** obtém nova leitura do Windows. O relatório é invalidado ao trocar disco/partição, atualizar inventário ou perder comunicação; nenhum resultado anterior fica exibido como atual. O item da sidebar conduz ao painel. Não há inclusão na fila.

## Testes e validações

- Testes sintéticos classificam estados `Healthy/Warning/Unhealthy/Unknown`, disco/volume, contadores com falhas, offline, desgaste, temperatura, erros não corrigidos e uso do volume.
- Parser testado com métricas válidas, ausentes e inconsistentes, `UInt64.MaxValue`, compatibilidade com JSON anterior.
- Testes MVVM cobrem Disco 0, nenhuma operação de escrita, invalidação em seleção/refresh/falha e mudança de dispositivo.
- Workflow Windows: análise AST de PowerShell (não executa comandos), build/test Debug e Release, publish self-contained win-x64 e instalador. **Verificar o resultado do HEAD no Actions; não inferir aprovação por commits anteriores.**
- Ainda pendentes: leitura em hardware real representativo SSD/NVMe/SATA/USB, sensores incompatíveis, smoke visual e avaliação com ferramenta/driver do fabricante.

## Decisão

**GDM-C13:** diagnóstico **somente leitura** com incerteza explícita. **NO-GO** para qualquer gravação física, autoteste ou reparo. `StorageWriteGate` permanece fechado. Próxima etapa do planejamento: GDM-C14.
