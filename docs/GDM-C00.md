# GDM-C00 — Preparação, baseline e arquitetura

Data: 14/10/2026. Branch de trabalho: `feat/full-disk-manager`. `main` permanece intocada. Este documento é o registro técnico do escopo C00, não a aprovação de lançamento comercial.

## Baseline verificado

- HEAD original: `fac02647e4cb1b0ea818260b9ceba520a91b32f0`.
- Solução: `GuiaSys.DiskManager.slnx`; .NET 10, WPF, MVVM, Windows x64; projeto de testes xUnit e runner administrativo VHDX.
- CI do baseline: runs `38340715825` e `38340717069`, **failure** no passo `Build Debug`; restore aprovado e etapas posteriores puladas. Falha **CS0246** para `InvalidDataException` em `Services/PowerShellDiskInventoryService.cs:151`, por falta de `using System.IO;`.
- Correção pontual C00: `using System.IO;` adicionado ao serviço, sem alterar o parser, o comportamento de inventário ou operações.
- Após desbloquear a compilação, o CI revelou fixtures de teste com `ExpectedDiskIdentity` incompatível com o contrato `uid:<UniqueId>` e ausência de `DriveLetter` em um cenário de `SetDriveLetter`. Os fixtures foram corrigidos para usar `DiskInfo.Identity` e uma letra válida, sem afrouxar o `SafetyService`.
- A documentação anterior registra 45/45 testes unitários Debug/Release e 32/32 etapas VHDX aprovadas **em 08/10/2026 em outro HEAD**. Esses resultados são evidência **histórica**, não validação automática da branch reconstruída.
- Artefatos existentes, assets de marca, scripts de build, CI e funcionalidades aproveitáveis foram preservados.

## Auditoria de riscos / backlog obrigatoriamente bloqueante

| Prioridade | Achado no código atual | Consequência / ação planejada |
| --- | --- | --- |
| P0 | `StorageOperation` retém identidade do disco, não uma identidade imutável da partição. | Troca/renumeração de partição entre fila e execução pode apontar outro objeto. Resolver C03 antes de gravação comercial. |
| P0 | `SafetyService` é chamado pelo ViewModel, mas não é um controle imposto pelo executor PowerShell; este valida sobretudo `UniqueId` do disco. | Janela TOCTOU e falta de defesa contra acesso direto ao executor. Centralizar controle autoritativo no executor C02/C03. |
| P0 | Confirmação reforçada em `MainWindow.xaml.cs` usa `operations[0].DiskNumber` embora a fila possa abranger múltiplos discos. | Consentimento inadequado para operações multi-alvo. Corrigir C03/C04. |
| P1 | Runner VHDX retorna `0` dentro do `try`; falha de cleanup pode marcar `success=false` no `finally`, mas não corrigir exit code. | Falso positivo em caso de vazamento de VHDX. Corrigir C04 e adicionar teste de falha forçada. |
| P1 | `MainViewModel.ApplyAsync` anuncia “verificadas pelo Windows”, mas não compara explicitamente pós-estado após cada operação. | Resultado indeterminado pode ser exibido como sucesso. Exigir pós-condições C02/C04. |
| P1 | Fila guarda operações isoladas sem preview do estado projetado nem resolução de dependências. | Combinações de operações podem falhar depois de passos irreversíveis. Planejador C02/C04. |
| P1 | Timeout de gravação fixo em 30 minutos e cancelamento sem journal de retomada/reconciliação. | Operações grandes podem ficar com desfecho desconhecido. Definir política C02/C04. |
| P2 | Bindings/seleção do mapa WPF precisam de testes de UI e acessibilidade, além de revisão de strings e estados. | Evolução C05/C06/C16. |
| P2 | Documentação histórica divulga gate anterior como se fosse atual, sem separar baseline da evolução. | Corrigir rastreabilidade no C00 (este registro e STATUS). |

**Decisão de segurança:** a correção de compilação **não** aprova operações físicas, licencia o produto nem resolve os P0. Na reconstrução, nenhuma operação destrutiva será testada em disco físico. Teste de integração de escrita deve usar VHDX descartável validado, runner administrativo opt-in, e evidência de desmontagem ao final. O pipeline CI **não** executa o gate elevado VHDX.

## Escopo C00 entregue ao repositório

1. Leitura do baseline, auditoria anterior e CI com evidência de erro.
2. Correção do erro de compilação CS0246 (import em C#).
3. Definição de arquitetura de destino em `docs/ARCHITECTURE.md` com fronteiras e invariantes, sem refatoração prematura.
4. Plano para GuiaSys Licensing GSL-v1 em `docs/LICENSING-PLAN.md` sem executar autenticação, ativação ou chamadas externas.
5. Revisão dos avisos de status para não confundir validações históricas com o HEAD atual.

## Gate de aprovação do C00

- [x] HEAD e `main` originais anotados, sem merge.
- [x] Diagnóstico de compilação sustentado por logs reais do CI.
- [x] Correção de import e definição arquitetural com rastreabilidade.
- [x] Sem implementação antecipada de C01 a C18; sem teste físico destrutivo.
- [x] CI da **base de código C00**: restore, Debug build/test, Release build/test, publish Windows x64 e instalador sem erros.
- [x] Reinspecionar logs e registrar hash de commit, resultado e contagem de testes.

## Resultado da validação de código

- Commit de código validado: `c0a220c66f938067f7e3b48f24eb384f5f8a97c5`.
- GitHub Actions runs **38666644551** e **38666648330**: conclusão `success` (ambos, eventos push/PR).
- Restore: aprovado. Build Debug: **0 warnings / 0 errors**. Testes Debug: **87/87** aprovados.
- Build Release: **0 warnings / 0 errors**. Testes Release: **87/87** aprovados.
- Publish **win-x64 self-contained**: aprovado. Compilação Inno Setup: **success**. Upload do portátil e do instalador: aprovados.
- O CI foi realizado em runner Windows hospedado. Nenhum gate VHDX elevado foi executado no C00; o gate anterior de 32/32 permanece evidência histórica da implementação legado, não prova dos riscos P0.

Os P0/P1 documentados permanecem backlog bloqueante para **gravação comercial**. O C00 cobre preparação, arquitetura e qualidade de compilação/testes, não certificação de funcionamento em discos físicos.

O commit apenas documental que registra estas validações também precisa obter CI verde antes de declarar o **HEAD final** aprovado.
