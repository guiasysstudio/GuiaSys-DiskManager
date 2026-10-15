# Arquitetura — GuiaSys Disk Manager

## Estado em 14/10/2026

A base 0.1.0-dev é uma aplicação **Windows x64 / .NET 10 / WPF**. O inventário, a interface MVVM, a fila, o `SafetyService`, o executor PowerShell e o runner administrativo VHDX já existem e devem ser preservados enquanto forem úteis. Esta seção descreve o **estado atual**, não a implementação de funcionalidades futuras.

O fluxo implementado é: `WPF/MVVM → StorageSnapshot → StorageOperation → SafetyService → fila → confirmação → novo inventário → executor PowerShell → Windows Storage API`. O executor usa script constante e parâmetros JSON por stdin. Esse desenho é reutilizável, mas **a segurança para uso comercial e discos físicos ainda não foi aprovada** (ver [GDM-C00](GDM-C00.md)).

## Arquitetura de destino (planejada; ainda não implementada)

```text
GuiaSys.DiskManager.Desktop (WPF: Views, ViewModels, acessibilidade)
  ├── GuiaSys.DiskManager.Application (casos de uso e orquestração)
  │    ├── IInventoryProvider / snapshots e capacidades
  │    ├── IOperationPlanner / preview do estado resultante
  │    ├── IOperationValidator / políticas antes e durante execução
  │    ├── IOperationJournal / estado, progresso, auditoria e pós-condições
  │    ├── IStorageExecutor / backend de Windows, permissão mínima
  │    └── ILicenseEntitlements / consulta de direito de uso
  ├── GuiaSys.DiskManager.Domain (identidades, geometria, riscos, operações)
  ├── GuiaSys.DiskManager.Windows (Storage API, volumes, VHDX, diagnósticos)
  └── GuiaSys.DiskManager.Licensing (adaptador GSL-v1, C01)
```

Os nomes são **fronteiras arquiteturais propostas**, não assemblies criados no C00. Evitar divisão artificial de projetos antes de existir teste de contrato para as interfaces. Nenhum módulo antecipado foi introduzido.

### Responsabilidades e regras de dependência

1. **Domain:** estruturas imutáveis de disco/partição/volume, identidade e geometria em setores/bytes com aritmética verificada; sem WPF, PowerShell ou rede.
2. **Application:** consulta, planejamento, dependências entre operações, pré-visualização do resultado, autorização/entitlements, orquestração e trilha de auditoria; não constrói scripts nem escreve em blocos.
3. **Windows:** inventário e executor por interfaces; implementação Windows nativa ou PowerShell conforme validação e capacidades reais. Executar apenas operações explicitamente autorizadas; nenhum comando arbitrário fornecido pela UI.
4. **Desktop:** exibição e interação; validações de UI são auxiliares, nunca a autoridade para escrever.
5. **Licensing:** adaptador externo GSL-v1 separado do domínio de armazenamento e de credenciais; indisponibilidade da plataforma não pode afetar inventário seguro nem permitir escalada de operações.
6. **Tests:** testes unitários de domínios/portas, testes contratuais do adaptador, integração VHDX descartável e validação de UI; nenhum teste automático destrutivo em hardware físico.

### Invariantes exigidas antes de habilitar gravação comercial

- Identidade estável **do disco e da partição**, com fingerprint de localização, extensão/offset, tipo, flags e, quando disponível, GUID/ID da partição e volume. Identidades ausentes/ambíguas devem bloquear mutações.
- Política de alvo **fail-closed** independentemente de número de disco: boot, sistema, Windows, EFI/MSR/Recovery, volumes montados/em uso e dispositivo do executável. Disco 0 continua bloqueado até revisão explícita.
- Revalidar imediatamente antes de cada passo **no componente que efetivamente executa**. Uma validação anterior feita apenas pelo ViewModel não é suficiente (janela TOCTOU).
- Toda fila precisa de **pré-visualização e validação do estado projetado**, conflitos e dependências; uma operação posterior não pode depender de estado antigo sem revisão.
- Confirmação deve identificar **todos os discos e partições afetados**; deve-se impedir que a confirmação de um disco autorize outro.
- Capturar o estado antes/depois; confirmar pós-condições externamente e distinguir: concluída, não executada, parcialmente executada e resultado indeterminado. Não prometer rollback automático de escrita no armazenamento.
- Persistir um journal com identificadores de operação, timestamps, fases e erros, sem chaves de licença, dados privados de arquivos ou seriais completos.
- Timeout/cancelamento têm semântica específica por operação: cancelar a interface não significa reverter uma gravação já iniciada. Em falha/power loss, reiniciar por reconciliação do estado, nunca retomar cegamente.
- Elevação sob privilégio mínimo e separação clara entre consulta sem administrador e execução privilegiada. **Licença válida não substitui autorização nem segurança do disco**.
- Testes devem provar que políticas são verificadas inclusive quando alguém chama diretamente o executor, fora da UI.

### Modelo de execução proposto

`Discover → Plan (sem gravar) → Validate → EntitlementCheck → ExplicitConsent → AcquireExclusiveTarget → Revalidate → Execute → Verify → Journal → Refresh`.

`AcquireExclusiveTarget` representa um controle futuro de serialização/bloqueio de alvo, a ser desenhado segundo as APIs disponíveis, **não** uma promessa de transação ACID ou bloqueio global de Windows. O journal é instrumento de diagnóstico e recuperação controlada, não cópia de segurança.

## Reuso e evolução incremental

Preservar: `DiskInfo`, `PartitionInfo`, `StorageSnapshot`, `ByteSize`, parser, mapa proporcional, fila, scripts constantes, logger, `SafetyService`, CI, testes xUnit e runner VHDX. Substituir ou extrair apenas após teste de regressão em módulo próprio.

O `PowerShellStorageOperationExecutor` é legado útil, mas hoje aplica mutações após identidade de **disco** e não verifica diretamente todas as políticas do `SafetyService`. O legado não deve ser declarado pronto para gravação comercial. A migração para contratos de execução segura ocorre em C02/C03/C04.

O mecanismo de clonagem, movimentação física, recuperação, migração de SO e conversões preservando dados fica fora do C00. Nunca construir essas funções com chamadas inseguras a diskpart/cópias de arquivos como substituto de mecanismo de blocos testado.

## Referências técnicas e licenças

Estudo conceitual realizado em `guiasysstudio/GuiaSys-DiskManager-References`:
- **GParted:** `src/OperationResizeMove.cc` (separa efeito visual e geometria da aplicação real); GPL-2.0-or-later indicado nesse arquivo.
- **KPMcore:** `src/core/operationstack.cpp`, `src/jobs/` (fila com preview e jobs por função); arquivos examinados incluem identificador GPL-3.0-or-later.
- **Partclone:** `src/partclone.c`, `src/checksum.c` (estratégia de cópia e verificação por sistema de arquivos/blocos); o arquivo `src/partclone.c` indica GPL-2.0-or-later.
- **GNU Parted:** `libparted/disk.c`, `libparted/device.c` (separação entre topologia do disco e dispositivo); a distribuição contém GPLv3 em `COPYING` — verificar licenças específicas de cada componente antes de qualquer integração.

Somente **ideias, requisitos e padrões arquiteturais** foram anotados. Nenhum código, asset, binário, driver, string literal protegida ou dependência dessas referências foi incorporado ao repositório do produto. O MiniTool é referência **de experiência de uso**, não fonte para replicar marca, código, drivers ou recursos proprietários. Uso de código copyleft em produto comercial exige análise de compatibilidade, obrigações de distribuição e revisão jurídica prévia.
