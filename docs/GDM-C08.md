# GDM-C08 — Redimensionamento de Partições (planejamento seguro)

**Data:** 15/10/2026. **Repositório:** `guiasysstudio/GuiaSys-DiskManager`. **Branch:** `feat/full-disk-manager`. **Base:** GDM-C07.

## Objetivo

Planejar redução/expansão de partições GPT de dados com base em **limites retornados pelo Windows** e na geometria contígua efetivamente não alocada. O módulo é conservador: **não executa alterações reais** porque `StorageWriteGate.ReleasedAfterSafetyAudit` permanece fixado em `false`.

## Origem e significado dos limites

O inventário Windows consulta `Get-PartitionSupportedSize -DiskNumber N -PartitionNumber P` apenas para partições de dados GPT não protegidas, em discos online e graváveis que não sejam Disco 0. Essa consulta fornece `SizeMin` e `SizeMax`, que descrevem limites suportados pelo sistema de arquivos/Windows no instante da captura. A API pode não retornar limites (driver, criptografia, FS não suportado, permissões, etc.). Nesses casos, a interface apresenta **indisponível** e não autoriza a preparação de um resize. O comando é de consulta, mas a documentação da Microsoft observa que ele pode iniciar o serviço **Optimize Drive (defragsvc)**, possivelmente tornando a leitura mais lenta.

Campos adicionados a `PartitionInfo`: `ResizeMinBytes`, `ResizeMaxBytes` opcionais. `DiskInfo.LogicalSectorSize` já é coletado desde C06. Campos ausentes em payloads legados permanecem nulos; pares parcialmente presentes ou semanticamente inválidos no inventário são rejeitados.

Documentação Microsoft: `Get-PartitionSupportedSize` e `Resize-Partition` do módulo PowerShell **Storage**.

## Algoritmo de elegibilidade

`PartitionResizePolicy.TryResolve` constrói a interseção de:
- limite mínimo e máximo fornecidos pelo Windows (`SizeMin`, `SizeMax`);
- tamanho do setor lógico informado pelo Windows (somente potências de dois entre 512 e 65.536 bytes);
- tamanho atual da partição, seu offset e extensão efetiva do disco;
- espaço contíguo **imediatamente à direita** da partição obtido por `StorageGeometry.Build`.

O mínimo é arredondado **para cima** até um múltiplo do setor e o máximo **para baixo**, nunca extrapolando a faixa do Windows. A expansão é limitada ao fim da extensão contígua adjacente, sem invadir outra partição. A redução considera o mínimo permitido pelo Windows (não estima o tamanho mínimo a partir do espaço livre informado pelo volume).

Resultados com geometria inconsistente, evidência ausente, sem opção de alteração ou tamanho atual não alinhado são recusados. `PartitionResizePolicy.Evaluate` recusa pedidos sem alteração, fora do intervalo e não alinhados.

## Integração com o aplicativo

- **Inventário PowerShell:** captura limites apenas quando aplicável; consulta que falha devolve valores nulos em vez de inferir suporte.
- **SafetyService:** valida os limites **também no inventário recém-lido** na fase de execução, além dos controles C00–C07 de identidade GPT, Disco 0, proteção de Windows/boot, licenciamento e confirmação.
- **OperationPlanner:** barra operações dependentes posteriores à alteração de geometria. O planejamento não presume que as demais partições permaneçam no mesmo estado após resize.
- **ContextOperationPolicy:** exibe redimensionamento como ação disponível **somente se existir um tamanho alternativo** compatível; partição sem GUID forte continua indisponível por identidade, e janela de tamanhos indisponível é comunicada ao usuário.
- **MainViewModel/WPF:** mostra tamanho atual, mínimo/máximo confirmados, setor lógico e direção possível (reduzir, expandir ou ambas). Botões **Usar mínimo / Usar máximo** preenchem o valor exato em **bytes**, não um valor arredondado em GB, e não executam operações. Se os limites desaparecerem após atualização, a ação permanece selecionada, porém desabilitada; o usuário não é redirecionado silenciosamente para outra operação.
- **OperationPostconditionVerifier:** após comando que porventura seja futuramente autorizado, o resultado só será classificado como `Verified` se o tamanho da partição for exatamente o pedido, GUID/offset/tipo e demais partições permanecerem íntegros, o sistema de arquivos permanecer o mesmo e `Get-Volume.Size` for conhecido **antes e depois** e tiver mudado na direção solicitada. Ausência da métrica ou FS inalterado em capacidade com partição redimensionada produz `Indeterminate` e **não autoriza repetir automaticamente**.
- **PowerShellStorageOperationExecutor:** defesa adicional **imediatamente antes** do cmdlet `Resize-Partition`: reconsulta `Get-PartitionSupportedSize`, recusa limites inválidos/mudados, exige setor lógico válido, tamanho diferente e alinhamento. As condições anteriores de identidade e layout protegido continuam vigentes. Esse segundo controle não elimina TOCTOU entre consulta e operação por outros processos.

## Casos de teste

Testes sintéticos do `PartitionResizePolicy`, do `SafetyService`, do `OperationPlanner`, do parser de inventário e do `MainViewModel`: limites ausentes/malformados, setor desconhecido, limites desalinhados, disco/partição protegidos, espaço adjacente ocupado, mínimo/máximo válidos, tamanho atual sem alteração, seleção de presets, stale refresh, partição substituída, não repetição de resize sem inventário e gate sempre fechado.

GitHub Actions Windows executa ainda uma verificação **não executante** da sintaxe dos dois scripts PowerShell incorporados, usando o parser AST nativo. Em seguida compila WPF em Debug e Release, executa xUnit, publica binário win-x64 self-contained, produz instalador Inno Setup e disponibiliza artefatos. A quantidade de testes e o SHA final devem ser verificados após o último commit.

## Limitações e homologação pendente

1. A API do Windows é a fonte de limites, mas não oferece uma garantia incondicional: volumes com BitLocker, VSS, arquivos imóveis, paginação, falhas de FS ou processos que alterem o armazenamento podem impedir um resize real.
2. Nenhuma redução/expansão foi executada. A passagem em testes unitários + build/instalador **não é homologação destrutiva**.
3. A execução PowerShell ainda exige homologação em VM isolada com VHDX descartáveis, auditoria de concorrência entre processos, proteção de volumes montados e tratamento de resultado indeterminado.
4. O script PowerShell consulta os limites novamente, mas **não** possui exclusão mútua contra todas as ferramentas externas do Windows. Ainda existe risco de corrida; gate fechado.
5. A interface WPF necessita smoke visual interativo em Windows 10/11, resoluções distintas e alvos elegíveis/inelegíveis.
6. O licenciamento GSL-v1 de produção precisa de configuração real de confiança e homologação antes de qualquer desbloqueio.

**Decisão:** desenvolvimento de preparação em C08; **NO-GO para qualquer gravação física ou VHDX**, inclusive em laboratório até que o plano específico de testes autorize.
