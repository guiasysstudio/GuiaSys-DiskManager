# Status verificável — 0.1.0-dev

> **Reconstrução GDM-C00 (14/10/2026):** os gates abaixo referem-se à **base 0.1.0-dev testada em 08/10/2026**, não significam aprovação da integração comercial nem dos riscos P0. O baseline `fac02647` falhou no CI por `CS0246`; o C00 corrigiu o import e registrou sua própria validação em [GDM-C00](GDM-C00.md). Operações de escrita em hardware físico **não estão liberadas para teste nesta reconstrução**. A situação de cada HEAD deve ser verificada nos Actions, sem presumir o resultado histórico.

## IMPLEMENTADO

- inventário, seleção sincronizada, detalhes e mapa proporcional;
- fila e operações allow-listed de disco/partição/volume;
- `SafetyService`, confirmação reforçada, revalidação e logs;
- branding, About, publish x64, instalador, CI e integração VHDX opt-in sem dependência de Hyper-V.

## TESTADO

- build e suíte xUnit: 45/45 testes aprovados em Debug e 45/45 em Release, zero warnings;
- abertura real sem elevação e inventário do Windows registrado em log;
- publicação Release self-contained `win-x64` em arquivo único;
- instalador Inno Setup 6.7.3 compilado, instalação silenciosa por usuário, abertura do app instalado e desinstalação silenciosa aprovadas.
- gate administrativo aprovado em 08/10/2026: 32/32 etapas com dois VHDX descartáveis, cobrindo GPT/MBR, online/offline, somente leitura, criação, NTFS/FAT32/exFAT, labels, letras, redução, expansão e exclusão;
- CI verde no HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`.

## VALIDADO

- regras puras de segurança, fila, parsing, classificação e identidade por testes automatizados;
- inventário somente leitura em Windows local.
- estado antes/depois de cada escrita confirmado por processos PowerShell independentes; ambos os VHDX foram desmontados e excluídos no `finally`.
- pipeline GitHub Actions reproduzindo build/test Debug e Release, publish e instalador em runner limpo.

## FORA DO ESCOPO VALIDADO

- matriz física Windows 10/11, DPI e USB descartável;
- clonagem, recuperação avançada e telemetria SMART proprietária.

Nenhuma função fora do escopo validado é apresentada na interface como suporte disponível. O gate VHDX não está pendente.

## Reconstrução GDM-C00 — CI da base de código

O commit `c0a220c66f938067f7e3b48f24eb384f5f8a97c5` aprovou as execuções Actions `38666644551` e `38666648330`: restore; builds Debug/Release sem warnings ou errors; testes **87/87 em ambas as configurações**; publish win-x64; instalador Inno; upload dos dois artefatos. Isto não revalida o gate VHDX histórico nem autoriza uso comercial de gravação física. Detalhes e pendências: [GDM-C00](GDM-C00.md).

## Reconstrução GDM-C01 — licenciamento (14/10/2026)

O cliente desktop GSL-v1 implementa consultas HTTPS da configuração do produto, ativação, avaliação, revalidação, desativação, JWS ES256 validado contra chave de confiança fixada, ID de instalação persistente e cache criptografado DPAPI. A UI oferece a janela **Licença**.

**Separação de gates:** testes automatizados com HTTP e JWS simulados são evidência funcional do cliente, mas **não** equivalem a ativação na produção. O build atual **não inclui a âncora pública de confiança aprovada para este produto**, logo bloqueia a concessão de licença real. É necessário obter do GuiaSys Licensing e confirmar externamente `projectId`, `signing.keyId`, `signing.publicJwk` e incorporar o fingerprint ao build. Nenhum segredo privado deve entrar no cliente. Operações de escrita continuam bloqueadas até C02–C04. Consulte [GDM-C01](GDM-C01.md) e o GitHub Actions no HEAD atual.

## GDM-C02 — Núcleo de gerenciamento (15/10/2026)

- Novo modelo de geometria com intervalos seguros, espaços contíguos e falha fechada para interseções/intervalos inválidos.
- Inventário Windows e mapa visual agora compartilham esse núcleo; cálculo determinístico e sem escrita.
- `IOperationPlanner` testa operações contra o snapshot projetado e detecta conflitos de letras, disponibilidade de extensão contínua, transições de flags e dependências que exijam novo inventário.
- UI apresenta resumo de planejamento, condição do disco e erros de conflito, além de impedir a inclusão de dependências inválidas.
- Execução de escrita permanece bloqueada por `StorageWriteGate` de C01. Problemas P0 de fingerprint de partição, validação no executor e confirmação multi-disco **não foram declarados resolvidos**. Sem gate físico; testes de escrita apenas VHDX opt-in em etapa posterior.
- Evidências detalhadas: [GDM-C02](GDM-C02.md) e status do pipeline Actions no HEAD desta branch.

## GDM-C03 — Identidade e autorização de alvos (15/10/2026)

- Captura do GUID GPT e tipo GPT, comparação canônica de identidade/topologia em operações destinadas a partições.
- Identidade MBR insuficiente para escrita: recusada, sem exceção automática.
- Proteções para layout integral (incluindo operações de criar, excluir e redimensionar) e identificação de tipos GPT não básicos.
- Executor exige sua própria autorização (gate físico, inventário novo, SafetyService, administrador, licença), sem depender exclusivamente do ViewModel; processo PowerShell revalida disk UniqueId e atributos críticos da partição.
- Confirmação reforçada cobre todos os discos afetados e o sinalizador legado de confirmação não ignora o diálogo.
- Escritas físicas e testes destrutivos continuam **NO-GO** até os itens pendentes de C04. Relatório: [GDM-C03](GDM-C03.md).

## GDM-C04 (15/10/2026)

Execução com journal JSONL durável, pós-condições verificadas e distinção entre recusa, sucesso comprovado e resultado incerto. Runner VHDX bloqueado. Nenhuma gravação liberada. Ver [GDM-C04](GDM-C04.md).

## GDM-C05 — Nova interface WPF (15/10/2026)

- Interface principal reestruturada: cabeçalho global, sidebar navegável, visão geral, mapa proporcional, tabela e detalhes de partições, editor contextual e fila inferior redimensionável.
- Estilos dark-mode, foco via teclado, tooltips em ações desativadas, navegação por seções e atalho F5.
- `ContextOperationPolicy` analisa alvo selecionado com as mesmas recusas de `SafetyService`; nenhuma disponibilidade na interface autoriza execução real.
- `MainViewModel` passa a atualizar ações contextuais, estado do item selecionado, seleção refletida no mapa, métricas do inventário, e oculta dispositivos obsoletos em falha de refresh.
- `ApplyCommand` desabilitado com gate de escrita fechado. GSL-v1, execução e journal C04 preservados.
- Testes automatizados de decisão contextual e integração do ViewModel adicionados. Smoke visual manual no Windows ainda pendente.
- Relatório: [GDM-C05](GDM-C05.md). **NO-GO** para gravações físicas e testes destrutivos VHDX.

## GDM-C06 — Inventário e visualização avançada (15/10/2026)

- Consulta read-only de setor físico/lógico e `Get-Volume.Size`/`SizeRemaining` opcionais.
- Associação conservadora de `Get-PhysicalDisk` por serial único; mídia e temperatura desconhecidas não são fabricadas. NVMe é rotulado como protocolo, não prova de SSD.
- `InventoryInsights` calcula capacidade particionada, espaço não alocado, maior faixa contígua e volume utilizado, com prevenção de overflow.
- GUI mostra resumos globais, capacidade do disco, progresso de uso do volume, métricas opcionais, proteção e classificação GPT confiável.
- Parser testável com campos opcionais ausentes/inválidos e fixtures sem operações de gravação.
- Gate de escrita e testes destrutivos continuam **NO-GO**. Detalhes: [GDM-C06](GDM-C06.md).

## GDM-C07 — Operações essenciais (15/10/2026)

- `EssentialOperationValidator` exige contrato coerente por tipo: disco ou partição, GUID/identidade, rótulo, tamanho, letra, filesystem e cluster; proíbe parâmetros ocultos/stale.
- O `MainViewModel` prepara comandos somente com os campos relevantes, sem confundir entradas anteriores; valida letras de D a Z como um único caractere e requer tamanho no resize.
- `SafetyService` checa limites de área contígua diretamente no inventário recente, recusa GPT ambíguo e impede criação MBR sem pós-condição independente confiável.
- Testes sintéticos e de ViewModel foram adicionados; nenhum teste de disco físico/VHDX foi liberado.
- Gate **NO-GO para gravações**. Ver [GDM-C07](GDM-C07.md).
