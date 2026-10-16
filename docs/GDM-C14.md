# GDM-C14 — Manutenção: análise segura de riscos (sem execução)

**Data:** 15–16/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C13.

## Entrega e limites

O C14 adiciona **pré-análise somente de metadados** de cinco categorias de manutenção, com detalhes do alvo selecionado, requisitos, riscos e impedimentos:

1. **Verificação de volume:** exibe apenas `Get-Volume.HealthStatus`, uso de espaço conhecido e mensagens de integridade **não verificada**; nenhum CHKDSK, `Repair-Volume`, dirty-bit query, teste de superfície ou reparo.
2. **Otimização:** contextualiza diferença entre ReTrim SSD e desfragmentação HDD quando a mídia for identificada expressamente pelo Windows; não mede fragmentação nem executa `Optimize-Volume`.
3. **Arquivos temporários:** sinaliza limitações e caminho/volume; não percorre diretórios pessoais, não enumera arquivos, não estima ganho e não exclui nada.
4. **Apagamento de espaço livre:** advertências sobre TRIM, setores remapeados, snapshots e criptografia; **não sobrescreve setores/arquivos nem promete sanitização**.
5. **Apagamento do disco inteiro:** identifica escopo, partições existentes e proteções do Disco 0/boot/sistema/EFI/MSR/Recovery; nenhuma gravação, Secure Erase, `Clear-Disk`, `Format-Volume`, `diskpart` ou comandos de firmware.

**Esta entrega não implementa motores operacionais de manutenção.** A API pública de relatório tem `MaintenanceReport.CanExecute => false` de forma incondicional, inclusive se a pré-análise geométrica não encontrar impedimentos. Não foram criados tipos de `StorageOperation`, executores ou caminhos alternativos que escapem ao gate existente `StorageWriteGate.ReleasedAfterSafetyAudit => false`.

## Contrato de dados e análises

`MaintenanceAssessment.Analyze(snapshot, diskNumber, partitionNumber, task)` é função determinística sobre o `StorageSnapshot`, sem processos, P/Invoke, acesso direto a dispositivos, varredura de arquivos ou manipulação de volumes.

- Garante seleção explícita: quatro tarefas são de **partição**; apenas apagar o disco inteiro aceita **disco sem partição selecionada**.
- Confere inventário e geometria via `StorageGeometry.Build`. Descrição do dispositivo, partição e indicadores de volume são somente os valores fornecidos pelo Windows.
- Rejeita alvos inexistentes, offline, geometria inconsistentes e ausência de identidades fortes para planejamento de comandos futuros. Uma pré-análise não substitui revalidação do executor.
- **Não presume suporte por barramento**: SSD/HDD somente quando `MediaType` fornece o tipo; NVMe/USB isolados não bastam. Sistema de arquivos desconhecido não é convertido em NTFS; outros tipos não são aprovados por inferência.
- Reaproveita as proteções de `SafetyService.IsProtected`; disco do Windows pode ser inspecionado com metadados para diagnóstico, mas não é tratado como alvo seguro de alterações.
- Se `VolumeCapacityBytes/VolumeAvailableBytes` não vierem do Windows, espaço livre e potencial de limpeza são **desconhecidos**.
- Mantém separadas as classes de risco: consulta de metadados, atividade que alteraria dados e apagamento destrutivo.
- `IsMetadataPlausible` significa **somente que o inventário não apresenta bloqueio conhecido**, nunca aprovação para execução.

## Interface WPF

- Nova navegação na sidebar: **Manutenção (prévia)**, abrindo o painel `C14 · MANUTENÇÃO`.
- Seleção explícita de tipo em ComboBox; fonte é sempre o disco/partição atualmente selecionado à esquerda.
- Botão **Avaliar riscos · sem executar** exibe tipo/risco, impedimentos e evidências. Nenhuma etapa adiciona ações à fila.
- Relatório invalidado ao modificar o tipo, o disco, a partição, atualizar o inventário ou ocorrer falha. O botão fica desabilitado sem disco ou durante operações ocupadas.
- Strings da interface deixam claro que não foi executado CHKDSK, TRIM, exclusão, formatação nem sanitização.

## Testes de aceitação

`MaintenanceAssessmentTests`: tarefas, riscos, `CanExecute=false`, bloqueio de Disco 0/boot, escopo, alvo removido, mídia ambígua, sistema desconhecido, volume protegido, métricas ausentes, disco offline, geometria inválida e relatório determinístico.

`MainViewModelUiTests`: seleção explícita, mudança de modo, descarte por refresh e troca de disco/partição, ausência de chamadas ao executor/fila, falha de inventário e desabilitação do comando sem alvo.

Os testes rodam na pipeline Windows (`dotnet build`, `dotnet test` Debug/Release, parse AST PowerShell, publish x64 e compilação Inno). As evidências do HEAD **precisam ser consultadas no GitHub Actions**; passagem da suíte não substitui homologação de mídia real e validação humana da interface.

## Pendências para manutenção real (fora do C14)

Executores individuais de manutenção, controle de privilégios/UAC, diferenciação SSD/HDD por dispositivo, recursos Win32 específicos, BitLocker, VSS/volumes em uso, plano de backup e recuperação, confirmação reforçada por alvo físico, integridade do motor de apagamento e homologação em dispositivos **exclusivamente descartáveis**. Em SSD, sobrescrita de setores lógicos não é evidência de eliminação física ou garantia certificável.

**Decisão C14: prévia e matriz de risco desenvolvidas; NO-GO para toda execução de manutenção que faça escrita, reparo ou apagamento.** Próxima etapa: GDM-C15 — Recuperação de partições, com estudo seguro antes de qualquer mecanismo de restauração.
