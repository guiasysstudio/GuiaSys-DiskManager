# Plano mestre — GuiaSys Disk Manager (GDM-F01…F24)

**Auditoria de planejamento:** 16/10/2026 · **base revisada:** `6e5e9e54150c5124c6f6c99f9d192925f7fbeb46` · **branch:** `feat/full-disk-manager` · **branch `main`: preservada**.

## Decisão e significado dos estados

O programa Windows WPF/.NET 10 x64 já iniciou em Windows real e inventariou cinco discos e nove partições no teste compartilhado pelo proprietário. O GitHub Actions da base acima aprovou **646/646 testes em Debug e 646/646 em Release**, com zero warnings/erros, publish e Inno. Isso é **evidência do código e da leitura**, **não** aprovação da segurança de gravação. O produto é `0.1.0-dev`. `StorageWriteGate.ReleasedAfterSafetyAudit=false` e a versão comercial permanece **NO-GO** (12 critérios no C18). Não executar modificações em discos reais do usuário como ensaio.

**Inventário de estados:** `FUNCIONAL (leitura)` = código confirmado + teste local apresentado; `PREPARADO/BLOQUEADO` = implementado para planejamento/PowerShell, mas **não utilizável para gravar**; `PRÉVIA` = apenas avaliação; `A IMPLEMENTAR` = motor de execução real ausente; `PENDENTE E2E` = exige prova em ambiente Windows/servidor real, sem afirmar sucesso.

## Inventário das opções existentes

| Área ou ação | Estado atual e limite | Etapa(s) futura(s) |
|---|---|---|
| Inventário de discos, partições, volumes, resumo, atualização F5 e seleção | **FUNCIONAL (leitura)** no Windows do proprietário; faltam compatibilidade abrangente, desconexão a quente e erros de drivers | F01–F03, F20 |
| Tabela, mapa por disco, proporções, uso disponível, atalhos e painéis | **FUNCIONAL parcial (UI)**; espaço, alinhamento, rolagem, hierarquia e DPI ainda precisam de melhoria e aceite | F01, F02, F20 |
| Saúde, temperatura, desgaste e indicadores do Windows | **PARCIAL / apenas leitura**; `Get-StorageReliabilityCounter` depende do dispositivo; não é monitor SMART completo | F03, F19 |
| Preparação de operações, prévia geométrica, fila, Desfazer, Limpar | **PREPARADO** em memória; conflitos e revalidação parcial testados; sem aplicação habilitada | F05, F06, F07, F14 |
| **Aplicar** / execução de comandos / confirmação reforçada / journal | **BLOQUEADO intencionalmente** no ViewModel, coordenador e executor; nenhum build de produção pode desbloquear por UI/licença | F06–F14, F22–F24 |
| Disco online/offline, ativar/desativar somente leitura | **PowerShell allowlist implementada / bloqueada**; Disco 0 e mídia do sistema protegidos | F08 |
| Inicialização **RAW vazio → GPT/MBR** | **PowerShell allowlist implementada / bloqueada**; não é conversão com dados | F09 |
| Criar e excluir partição | **PowerShell allowlist GPT / bloqueada**; escrita MBR carece de identidade/pós-condições próprios | F10, F15 |
| Formatar NTFS/FAT32/exFAT, letra, remover letra, renomear volume | **PowerShell allowlist / bloqueada**, requer autorização, controles e testes reais em mídia virtual | F11 |
| Redimensionar (reduzir/expandir) | **Planejamento Windows `Get-PartitionSupportedSize` + executor protegido**, sem movimentar dados manualmente | F12 |
| Mover partição | **PRÉVIA geométrica**, sem motor de cópia, transação ou rollback | F16 |
| Analisar GPT↔MBR com dados | **PRÉVIA**, sem conversão preservando dados; RAW vazio está listado separadamente | F17 |
| Clonagem de disco e de partição | **PRÉVIA**, sem motor de cópia/setores, comparação de conteúdo ou prova de boot | F18 |
| Migrar Windows para outro disco | **DIAGNÓSTICO**, sem migração, BCD, WinRE, EFI, VSS, BitLocker ou ensaio de inicialização | F19 |
| Partições perdidas | **Comparação de snapshots GPT na memória**; não busca assinaturas no disco nem recupera arquivos/partições | F21 |
| Manutenção, verificação, otimizar TRIM/defrag, limpeza, apagar espaço livre ou disco | **PRÉVIA**, não executa reparo, limpeza ou sanitização | F22 |
| Licença GSL-v1: ativação, revalidação, avaliação, desativação, DPAPI/ES256 | **Cliente implementado + chave pública fixa** em `licencas.guiasys.com.br`; **PENDENTE E2E** com licença própria, revogação/offline; trial do projeto estava desativado | F04 |
| Sobre e atualização via GitHub | **Consulta manual** a release pública estável; abre página oficial, **não baixa/instala automaticamente**. Repo privado/release ausente impedem consulta anônima | F23 |
| Instalador x64, hashes SHA-256 e workflow CI | **Compilam**; assinatura Authenticode e homologação real de upgrade/rollback/Win10/11 pendentes | F23–F24 |

As opções bloqueadas na captura não são só defeitos visuais: refletem uma política de segurança válida. **Jamais reabilitar botões para mascarar ausência de backend ou remover gate sem auditoria.**

## Bloco A — Base funcional e UX (sem escrever em discos)

**F01 — Interface principal profissional.** Ajustar área de tabela, mapa multi-disco, proporções, painéis ajustáveis, informação de partições pequenas, rolagem, seleção e foco, hierarquia parecida com gerenciadores convencionais sem copiar recursos proprietários. Eliminar redundância de atalhos/filas; mensagens visíveis diferenciando `prévia`, `disponível` e `bloqueado`. **Aceite:** capturas e smoke de teclado/rato em 100/125/150/200% DPI, múltiplos monitores e janela pequena, sem perda de dados visuais.

**F02 — Inventário Windows e geometria confiáveis.** Cobrir GPT/MBR/RAW, NVMe/SATA/USB, VHDX, discos removíveis, partições ocultas, não alocado, identificação de clones, mudanças de numeração, capacidade real, alinhamento e setores 512/4096. Revisar falhas e cancelamento; identidades ambíguas continuam com **apenas leitura**. **Aceite:** fixtures + leituras em PCs diferentes, nenhum caminho inventa número, geometria, capacidade ou estado.

**F03 — Diagnóstico somente leitura e detalhes.** Estado, tipo de mídia, volumes, capacidade ocupada, SMART/reliability real quando disponível; separar `indisponível` de `saudável`, exibir avisos contextualizados e exportação **sem segredos**. **Aceite:** provas de Windows sem SMART, volumes RAW e mídias com métricas incompletas; nada dispara reparo/escrita.

**F04 — Homologação do GuiaSys Licensing.** Licença descartável de teste gerada no Admin; ativação, validação, limite de dispositivos, expiração, suspensão/revogação, desativação, falta de rede, DPAPI e relógio adulterado, com versões de contrato reais. Testar sem compartilhar licença/certificados privados no GitHub. **Aceite:** evidência do cliente Windows e do painel oficial com credenciais ocultas; falha fechada e zero relação entre licença válida e autorização física.

## Bloco B — Núcleo transacional e segurança

**F05 — Matriz de compatibilidade e perigos.** Definir suporte por combinação `Windows 10/11 × GPT/MBR × NTFS/FAT32/exFAT × volume básico/dinâmico/Storage Spaces × disco virtual/físico × BitLocker/boot/EFI/WinRE`. Categorias não homologadas **não escrevem**. Revisar política de Disco 0, disco de boot, instalação executável e partições protegidas. **Aceite:** matriz versionada e testes negativos antes de qualquer habilitação.

**F06 — Planejador/filas/execução revisados.** Revisar entrada, contratos, conflitos, identidade disco/partição, prévia projetada, dependências, cancelamento, journal durável, confirmação por todos os alvos, divergência de metadados e revalidação imediatamente antes da escrita. Nenhum `Success` de PowerShell substitui pós-condição independente. **Aceite:** injeção de falhas, corrida TOCTOU, crash, resultado indeterminado, sem auto-retry destrutivo.

**F07 — Laboratório isolado VHDX.** Auditar e adequar `tests/integration/Test-VhdxOperations.ps1` e runner legado à política vigente. Criar ambiente **de teste dedicado** em VM descartável, com identificação inequívoca de VHDX criado pelo ensaio, limpezas verificáveis, sem caminho de bypass no aplicativo comercial. Solicitar aprovação humana antes de qualquer teste elevado. **Aceite:** provas GPT/MBR virtuais, antes/depois e desmontagem sem tocar mídia física. Gate comercial continua fechado.

## Bloco C — Operações essenciais: uma categoria por homologação

**F08 — Estado do disco:** online/offline e somente leitura em VHDX testado; nunca no Disco 0/sistema. **Aceite:** ida/volta, rejeição de dispositivos perigosos e pós-condição.

**F09 — Inicialização segura:** somente disco `RAW` comprovadamente vazio para GPT/MBR; impor setor e limites do MBR. **Aceite:** VHDX positivo/negativo e verificação independente da tabela criada.

**F10 — Criar e excluir partições GPT:** espaço contíguo, alinhamento, preservação de partições vizinhas, identidade GUID e pós-condição, confirmação de destruição. **Aceite:** VHDX com múltiplas partições, falha intermediária e proteção.

**F11 — Formatação e metadados:** NTFS/FAT32/exFAT (apenas quando Windows suportar), label, letra, remoção de letra, unidade de alocação e formatação rápida/completa. **Aceite:** testes VHDX com drive-letter conflicts, mídia ocupada, atributos especiais e dados irrecuperáveis claramente sinalizados.

**F12 — Reduzir/expandir partição:** respeitar `Get-PartitionSupportedSize`, sistema de arquivos, alinhamento, limitação de espaço contíguo e pós-condição de volume/FS. **Aceite:** VHDX positivo/negativo, journaling em interrupção, limiares exatos; nenhuma promessa de mover partição ao redimensionar.

**F13 — Editor de operações e filas:** wizard por ação, resumo claro do estado original/projetado, campos condicionalmente exigidos, prévia visual, risco/impacto, confirmação por disco/partição e sequência executável somente quando a categoria de operação passou nos gates. **Aceite:** teclado, mensagem de bloqueio específica, cancelar/desfazer planejamento e nenhum rótulo de sucesso sem pós-condição.

**F14 — Homologação de execução por operação:** matriz consolidada de comandos acima sob VHDX, prova de reconstrução de journal, enumeração de identidades repetidas e rejeição de concorrência. **Aceite:** autorização humana formal restrita ao ambiente e à categoria; **nunca desbloqueio global irrestrito ou escrita em discos de trabalho**.

**F15 — MBR já particionado:** implementar identidade e pós-condições de partição MBR independentes de GUID GPT, com testes de numeração, assinatura do disco, offset e tipo, somente quando segurança permitir. **Aceite:** testes VHDX e política explícita; sem isso, mutações MBR existentes continuam indisponíveis.

## Bloco D — Recursos avançados (motores novos, risco elevado)

**F16 — Mover partição:** projetar motor de movimentação *real*, integridade de dados, interrupção/retomada verificável, checkpoints/backup e recuperação. **Aceite:** cópias de VHDX com hashes/volumes verificados em erro e sucesso; não confundir prévia geométrica com implementação.

**F17 — Converter GPT ↔ MBR sem apagar dados:** pesquisar e escolher suporte real por cenário; proteger boot/sistema, limite MBR e partições especiais. Conversão RAW vazia não conta. **Aceite:** imagem de disco descartável, migração verificável de partições e boot se suportado; cenários impossíveis explicitamente bloqueados.

**F18 — Copiar/clonar partição/disco:** motor de cópia com estratégia baseada em blocos ou sistema de arquivos quando licenciada/compatível, origem/destino fortes, erros de leitura, destino limitado, status, checkpoints e verificação. **Aceite:** VHDX + checksums, falha de energia/interrupção simulada, boot se prometido.

**F19 — Migrar sistema Windows:** ferramenta própria com pré-checks BitLocker/TPM/EFI/MSR/WinRE/BCD/VSS e ensaio de boot real em VM. **Aceite:** máquina virtual de destino inicializa autonomamente e mantém estrutura correta; não oferecer ao usuário antes disso.

**F20 — UX e qualidade cruzada:** redimensionamento/DPI, animações não obrigatórias, acessibilidade NVDA/Narrator, contraste, desempenho e testes de falhas e refresh em Windows 10/11. **Aceite:** matriz visual assinada, sem binding crash/overflow, inclusive em muitos discos.

**F21 — Recuperar partições perdidas/arquivos:** motor de análise **read-only em blocos** e verificação de assinaturas, tabela de evidências, recuperação para **outra mídia** com validação; nunca presumir que ausência no snapshot equivale a recuperação viável. **Aceite:** VHDX com perdas simuladas, casos conflitantes e nenhuma escrita no disco de origem.

**F22 — Manutenção real:** chkdsk/reparo, TRIM/otimização, limpeza, exclusão definitiva e apagamento seguro são fluxos **separados** com consentimento e prova. Sanitização SSD depende do controlador e não pode prometer irrecuperabilidade genericamente. **Aceite:** escopo autorizado, backup, limites de filesystem, status auditável, VHDX/VM e mídia descartável quando expressamente aprovado. Não misturar limpeza de arquivos do usuário com gerenciamento de partições.

## Bloco E — Produto e lançamento

**F23 — Instalador, distribuição e atualizações.** Assinatura Authenticode do instalador e EXE, checksums com procedência, identidade do publicador, atualização e rollback preservando DPAPI/cache/logs, versões Win10/11; decidir se a atualização permanece **manual** (estado atual) ou se haverá mecanismo assinado e autenticado separado. **Aceite:** instalação/upgrade/desinstalação/downgrade em máquinas limpas, nenhuma substituição de binário sem validação.

**F24 — Auditoria final e matriz comercial.** Revisar conjunto real de features contratadas, privacy/LGPD, créditos/licenças de componentes, regressão completa Debug/Release, ensaio humano em Windows 10/11, recuperação dos logs, evidências de VHDX e **gates C18-01…12** por versão/sha. **Aceite:** todos os itens exigidos para a edição efetivamente oferecida aprovados por responsável humano; só então construir e assinar release comercial. Nenhum CI verde isolado constitui GO.

## Funcionalidades adicionais de gerenciadores como o MiniTool — ainda fora da interface atual

A semelhança visual **não implica paridade funcional**. Os itens abaixo **não aparecem no enum de operações executáveis do programa atual** e, portanto, não podem ser anunciados como entregues. Precisam de priorização de produto antes de entrar no escopo de uma edição futura:

| Capacidade candidata | Situação e dependência |
|---|---|
| Mesclar/dividir partições sem perder dados | **NÃO IMPLEMENTADA**; requer movimentação/cópia de arquivos, limites NTFS/partição, backup e recuperação |
| Copiar partição com sistema de arquivos, em vez de prévia | **NÃO IMPLEMENTADA**; dependerá do motor F18 |
| Alinhamento 4K/reparo de desalinhamento | **NÃO IMPLEMENTADO**; não é apenas mudar a posição apresentada no gráfico; requer movimentar setores e garantir consistência |
| Converter sistema de arquivos preservando dados | **NÃO IMPLEMENTADO**; não prometer conversões NTFS/FAT32/exFAT irrestritas |
| Ocultar/exibir partição, propriedades avançadas, GUID/tipo, reconstruir MBR/BCD | **NÃO IMPLEMENTADO**; configurações de boot e atributos GPT exigem políticas separadas |
| Teste de superfície, benchmarking e comparação de performance | **NÃO IMPLEMENTADOS**; não confundir indicadores SMART disponíveis com varredura setorial |
| Ambiente inicializável WinPE/recuperação offline | **NÃO IMPLEMENTADO**; poderá ser necessário a operações no volume do Windows, com assinatura/mídia/licenças próprias |
| Suporte total a discos dinâmicos, Storage Spaces, BitLocker bloqueado, ReFS, RAID de hardware | **NÃO HOMOLOGADO**; inventariar não equivale a autorizar mudanças |
| Backup/restore de imagens e reparo de sistemas de arquivos | **NÃO IMPLEMENTADOS** como motores verificáveis; avaliar escopo, licenciamento e requisitos de recuperação |

**Decisão de produto pendente:** quais capacidades adicionais serão obrigatórias na edição comercial e quais serão oferecidas somente em versões futuras. Não usar as ferramentas/código de outros produtos como garantia de licença ou segurança para copiar implementações.

## Priorização e releases

**Primeira versão utilizável e segura:** F01–F15, F20, F23, F24, com todas as operações básicas individualmente homologadas ou **explicitamente marcadas não suportadas**. O bloqueio físico não será removido prematuramente. Não incluir como “disponível” as ações F16–F19, F21–F22 até cada motor e teste de aceite existir.

**Evoluções avançadas:** F16–F19 e F21–F22 são grandes projetos próprios; exigem implementação de motor, política de backup/restauração e orçamento de testes distinto. Se forem requisitos indispensáveis à primeira edição, a primeira release deve aguardar todas essas etapas.

### Regra de execução para cada Fxx

1. Abrir issue/escopo com inventário de classes/métodos, riscos e decisões; não confundir testes sintéticos com Windows real.
2. Implementar no **`feat/full-disk-manager`**, mantendo `main` preservada.
3. Adicionar testes positivos, negativos, de interrupção e de autorização e revisão do XAML/UIA.
4. CI Debug + Release + publish + instalador + artefatos; reportar commit e testes do HEAD.
5. Homologação manual segura no VS Code/VM, com logs editados para remover seriais/dados pessoais/licenças; nenhuma gravação física sem gate e autorização específicos.
6. Registrar **GO de engenharia / NO-GO comercial** quando cabível e só prosseguir à etapa dependente depois do aceite.

**Próxima ação proposta: GDM-F01**, ajuste completo da interface (tabela, mapa, cores, disposição e barra inferior) usando capturas do proprietário como referência; **sem modificar os mecanismos de escrita**. Depois F02 e F03, para obter base de leitura plenamente estável antes de qualquer teste de execução.
