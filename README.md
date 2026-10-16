# GuiaSys Disk Manager

Gerenciador gráfico de discos e partições para Windows 10 e Windows 11 x64, desenvolvido pela **GuiaSys Studio** em C# 14, .NET 10, WPF e MVVM.

> **GDM-C18 — NO-GO comercial.** Versão de desenvolvimento `0.1.0-dev`. O produto possui mecanismos de leitura, prévias e componentes de escrita protegidos por um gate global **fechado**. Nenhuma licença, teste unitário ou build CI autoriza gravações físicas, atualização automática ou distribuição comercial. Confiança GSL-v1 real, assinatura Authenticode e homologação Windows/hardware estão pendentes. Consulte [GDM-C18](docs/GDM-C18.md).

## Recursos atuais

- inventário de discos, partições e volumes com modelo, série, barramento, saúde, GPT/MBR/RAW, flags de boot/sistema, filesystem, label, tamanho e espaço livre;
- mapa gráfico proporcional com EFI, MSR, NTFS, FAT32, exFAT, Recovery, RAW e espaço não alocado, agora derivado da mesma geometria rigorosa do inventário;
- fila com planejamento determinístico somente leitura, detecção de conflitos, prévia de estado de metadados, efeitos em discos independentes e barreiras para geometria incerta; executar/aplicar permanece bloqueado;
- operações allow-listed: online/offline, read-only, inicialização segura de disco RAW, criação/exclusão/formatação, label, letra e redimensionamento;
- proteção central de Disco 0, C:, Windows, boot, System, EFI, MSR, Recovery e identidade alterada;
- logs JSON Lines em `%LOCALAPPDATA%\GuiaSys\DiskManager\Logs`, com retenção de 30 dias;
- publicação portátil self-contained `win-x64` e instalador Inno Setup.

Clonagem, recuperação avançada de partição e interpretação SMART proprietária não são anunciadas como disponíveis: exigem mecanismos e validação adicionais para não criar uma falsa garantia.

## Build rápido

```powershell
dotnet restore .\GuiaSys.DiskManager.slnx
dotnet build .\GuiaSys.DiskManager.slnx -c Debug
dotnet test .\GuiaSys.DiskManager.slnx -c Debug
dotnet publish .\src\GuiaSys.DiskManager\GuiaSys.DiskManager.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\portable
```

Documentação: [arquitetura](docs/ARCHITECTURE.md), [segurança](docs/SECURITY.md), [build](docs/BUILD.md), [testes](docs/TESTING.md), [operações](docs/OPERATIONS.md), [auditoria](docs/AUDIT.md) e [status verificável](docs/STATUS.md).

## Estado verificado

- 45/45 testes aprovados em Debug e 45/45 em Release, sem warnings;
- 32/32 etapas do gate administrativo VHDX aprovadas, usando somente discos virtuais descartáveis;
- CI verde para o HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`.

## Segurança

Os testes automatizados não escrevem em hardware. O gate de integração administrativa está concluído e aceita somente VHDX temporários criados pelo próprio runner, valida o alvo em várias camadas e sempre os desmonta e exclui no bloco `finally`. Não execute operações de armazenamento sem backup verificado.

## Reconstrução modular GDM-C00 → GDM-C18

A evolução para produto comercial está em etapas. A preparação C00 corrige um bloqueio de compilação, define a [arquitetura de destino](docs/ARCHITECTURE.md), documenta o [plano de licenciamento GSL-v1](docs/LICENSING-PLAN.md) e os [riscos/gates C00](docs/GDM-C00.md). **Os resultados de 45 testes e 32 etapas VHDX citados acima pertencem ao HEAD funcional de 08/10/2026** e não substituem CI do novo HEAD. O **cliente GuiaSys Licensing GSL-v1 foi implementado no GDM-C01** com UI, ativação, trial, validação online/offline ES256, cache DPAPI e desativação. **Homologação real ainda bloqueada**: faltam o Project ID e a chave pública de assinatura do produto, que precisam ser fixados no build; a segurança das gravações em discos físicos também permanece pendente de C02–C04. Consulte [GDM-C01](docs/GDM-C01.md). O [GDM-C02](docs/GDM-C02.md) implementa o núcleo de geometria e planejamento de operações, com validação de espaço livre contíguo, conflito de letras e dependências incertas, sem qualquer escrita em disco.

### C03 — identidade e segurança de operações

O núcleo de [Segurança GDM-C03](docs/GDM-C03.md) vincula a partição GPT a GUID, geometria e tipo, bloqueia alvos protegidos inclusive em chamadas diretas ao executor e exige confirmação textual envolvendo todos os discos afetados. Isso **não habilita escrita física**: o gate `StorageWriteGate` segue fechado até a conclusão dos critérios C04. Partições MBR sem GUID próprio não podem ser alteradas nesta etapa.

### GDM-C04

Journal de execução, reconciliação de operações e verificações independentes de pós-condição foram adicionados. **O gate de escrita permanece fechado**, inclusive para o runner VHDX. Consulte [GDM-C04](docs/GDM-C04.md).

### GDM-C05 — Nova interface gráfica WPF

Uma nova estrutura visual inclui sidebar de navegação funcional, painel de discos, mapa de partições proporcional, grade de volumes, painel de operações contextuais e fila inferior com altura ajustável. Ações bloqueadas exibem o motivo, entradas do formulário aparecem conforme a operação e o comando Aplicar permanece desabilitado enquanto o gate de escrita estiver fechado. Atalho **F5** atualiza o inventário em modo leitura. Nenhuma nova dependência visual externa foi adicionada. Consulte [GDM-C05](docs/GDM-C05.md) para ver os critérios de smoke visual ainda pendentes.

### GDM-C06 — Inventário avançado e capacidade real

O inventário agora oferece métricas opcionais de setor físico/lógico, tecnologia e conexão do dispositivo, classificação GPT de partições EFI/MSR/Recovery e estatísticas de ocupação do volume quando reportadas pelo Windows. O painel diferencia **espaço não alocado do disco** de **espaço livre dentro do volume**, com resumos de capacidade e uso, além de alertas de pouco espaço. Valores desconhecidos não são transformados em zeros. O gate de gravação continua fechado. Veja [GDM-C06](docs/GDM-C06.md).

### GDM-C07 — Operações essenciais e contratos de segurança

A preparação de operações agora usa um contrato estrito por tipo, incluindo validação exata de letras, tamanhos, rótulos e parâmetros de formatação, sem reaproveitar campos ocultos de ações anteriores. O `SafetyService` revalida o maior espaço contíguo para criação/expansão e impede alterações de layout quando a identidade GPT está incompleta. A criação de novas partições MBR permanece suspensa até existir verificação independente da identidade da partição; inicialização MBR em RAW vazio ainda pode ser planejada. **Gravações continuam bloqueadas.** Leia [GDM-C07](docs/GDM-C07.md).

### GDM-C08 — Redimensionamento com limites reais do Windows

O painel de redimensionamento apresenta mínimo/máximo e tamanho atual quando `Get-PartitionSupportedSize` retorna uma faixa confiável para partições de dados GPT. O aplicativo cruza o intervalo com o setor lógico e com o espaço contíguo não alocado, sem estimar a capacidade de redução apenas pelo espaço livre do volume. Presets **Usar mínimo/Usar máximo** preenchem os bytes exatos. A política e o script PowerShell revalidam os limites antes de qualquer possibilidade de execução. Se as informações faltarem, redimensionamento não é preparado. **A escrita continua bloqueada.** Consulte [GDM-C08](docs/GDM-C08.md).

### GDM-C09 — Movimentação de partições (prévia geométrica, não executável)

A interface permite escolher uma partição GPT, informar uma posição absoluta e visualizar a **prévia de movimentação** para uma área integralmente não alocada. O programa verifica identidade forte, Disco 0, partições protegidas, alinhamento de 1 MiB, limites e colisões; pode sugerir uma faixa segura próxima. **Não há cópia física, movimentação de bytes ou exclusão/recriação de partições**. A ação não entra na fila, e a camada de segurança, o coordenador e o executor a recusam independentemente do gate global. Esta etapa **não promete suporte executável a mover partições**. Consulte [GDM-C09](docs/GDM-C09.md).

### GDM-C10 — Diagnóstico GPT / MBR (somente leitura)

O painel analisa o esquema atual e o destino GPT ou MBR, verifica tamanho do setor, limite LBA de 32 bits do MBR, quantidade de partições, identidade, proteção e geometria, e apresenta alertas de perda de dados/boot. Distingue **inicializar RAW vazio** de **converter discos com partições**. Não usa `Clear-Disk`, `MBR2GPT`, troca de GUID ou exclusão/recriação de partições. **Conversão com preservação de dados não implementada**; nenhum diagnóstico gera operação de escrita. O gate de gravação permanece fechado. Detalhes: [GDM-C10](docs/GDM-C10.md).

### GDM-C11 — Clonagem de discos e partições (prévia somente leitura)

Um novo painel permite escolher **origem** (disco ou partição) e **disco de destino** distintos e estudar a geometria da clonagem. Para discos inteiros, a origem GPT/MBR particionada exige destino RAW vazio e capacidade igual/superior a todos os bytes do disco original. Para partição GPT básica, o diagnóstico procura extensão **inteiramente não alocada**, contígua e alinhada em um destino GPT. Disco 0, inicialização/EFI/MSR/Recovery, IDs ambíguos, sobreposição e incompatibilidade dos setores são tratados conservadoramente. A análise invalida resultados em refresh ou mudança da seleção. **Nenhum dado é copiado**, não existe `CloneDisk`/`ClonePartition` na fila/allowlist, e `StorageWriteGate` continua fechado. Detalhes em [GDM-C11](docs/GDM-C11.md).

### GDM-C12 — Migração do Windows (diagnóstico sem escrita)

O painel permite selecionar a origem do Windows (inclusive **Disco 0 apenas para inspeção**) e um disco de destino **RAW vazio**. O diagnóstico verifica topologia GPT/UEFI (partição Windows NTFS, ESP FAT32, MSR, WinRE), capacidade integral, setores e identidade dos dispositivos, detecta boot potencialmente distribuído por discos e apresenta **BitLocker, BCD, firmware, TPM e VSS como não verificados**. O modo Legacy/MBR exige homologação própria. O checklist não faz backup, cópia, conversão nem preparação de boot; `CanExecute=false` e gate global de escrita fechado. Leia [GDM-C12](docs/GDM-C12.md).

### GDM-C13 — Saúde e diagnósticos (somente leitura)

O painel C13 apresenta estados de saúde Windows dos discos/volumes, temperatura e contadores opcionais de desgaste, horas ligadas e erros reportados por `Get-StorageReliabilityCounter`. Associação física somente por série única; métricas sem suporte ficam **não disponíveis**. O avaliador emite avisos e sempre informa que **S.M.A.R.T. integral, autotestes, superfície e reparo não foram executados**. Selecione um disco e clique **Analisar indicadores**; **F5** atualiza as medições. Não habilita escrita. Ver [GDM-C13](docs/GDM-C13.md).

### GDM-C14 — Manutenção (prévia de riscos, sem execução)

O painel **Manutenção (prévia)** analisa cinco categorias: verificação de volume por metadados do Windows, potencial de otimização, revisão de arquivos temporários, apagamento do espaço livre e apagamento integral do disco. O relatório diferencia alterações, risco destrutivo, volumes protegidos, mídia/FS desconhecidos, identidade e uso do volume, mas **não executa CHKDSK, TRIM, varreduras, exclusão, limpeza ou apagamento**. A fila e a allowlist de escrita não incluem ações de manutenção; `CanExecute=false` para todas as prévias e o gate global permanece fechado. Detalhes: [GDM-C14](docs/GDM-C14.md).

### GDM-C15 — Investigação de partições perdidas (comparação somente leitura)

O painel **Partições perdidas (prévia)** permite **capturar explicitamente um baseline de entradas GPT** de um disco íntegro, atualizar o inventário (F5) e comparar o estado posterior do **mesmo dispositivo por UniqueId**. São destacados GUIDs persistentes, alterados ou ausentes; faixas anteriores que agora estão não alocadas são **apenas indícios de alteração da tabela**, nunca prova de dados recuperáveis. Referência e laudo ficam em memória e são invalidados na troca de alvo ou falha. **Não há varredura de setores, recuperação de arquivos, restauração GPT nem gravação.** Leia [GDM-C15](docs/GDM-C15.md).

### GDM-C16 — Acessibilidade e navegação segura da interface WPF

O mapa de partições agora usa **botões acessíveis por teclado** (Tab + Enter/Espaço), com rótulos de automação, tamanhos/offsets explícitos e seleção `INotifyPropertyChanged` que não recria os controles focados. **Ctrl+1…8** navega pelos oito painéis e F5 continua apenas relendo o inventário. A interface permanece somente leitura para análises; o planejamento e o bloqueio de gravações são preservados. Teste interativo de foco, Narrator/NVDA e escalas Windows ainda requer homologação humana. Consulte [GDM-C16](docs/GDM-C16.md).

### GDM-C17 — Instalador, verificações de atualização e pacote Windows

O menu **Sobre → Verificar atualização** consulta manualmente releases públicas estáveis do repositório oficial. URLs, versões e nomes de instalador são validados de modo estrito antes de habilitar **Abrir versão oficial**. **Não há download ou instalação automática.** O Inno Setup mantém o AppId e a pasta de dados pessoais fica fora da desinstalação; o build valida versão de executable/instalador e publica checksums SHA-256, também verificados no CI. Repositório privado não oferece releases públicas anônimas: a checagem do cliente não usa token. A assinatura Authenticode, testes de upgrade/desinstalação e lançamento comercial aguardam C18. Ver [GDM-C17](docs/GDM-C17.md).

### GDM-C18 — Auditoria de lançamento comercial (NO-GO)

O gate C18 gera um relatório JSON verificável no GitHub Actions com **12 requisitos obrigatórios de liberação**. O código de avaliação consulta o estado compilado do gate de escrita (`false`) e a ausência de fingerprint GSL público fixado no build; testes sintéticos não podem liberar essas barreiras. O script `tools/Test-CommercialReleaseGate.ps1` mede versão/assinatura local quando disponíveis, **não inventa validações externas** e bloqueia `-RequireCommercialGo`. A pipeline publica o artefato `GuiaSys-DiskManager-C18-NO-GO-audit`. O pipeline verde é um resultado de engenharia, **não um lançamento autorizado**. Veja o [relatório GDM-C18](docs/GDM-C18.md).
