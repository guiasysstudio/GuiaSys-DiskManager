# GDM-C05 — Nova interface gráfica WPF

**Data:** 15/10/2026. **Repositório:** `guiasysstudio/GuiaSys-DiskManager`. **Branch:** `feat/full-disk-manager`. **Base:** GDM-C04.

## Objetivo

Modernizar a interface WPF preservando o inventário, a fila, o planejador, as proteções dos discos/partições e a integração de licença GSL-v1. Este módulo modifica somente a experiência visual e a disponibilidade de preparação; **não libera nenhuma operação destrutiva.**

## Layout implementado

- **Cabeçalho global** com logo existente, identidade GuiaSys, rótulo explícito do modo seguro, atualização F5, logs, licença e sobre.
- **Barra lateral persistente** com navegação funcional para Visão geral, mapa proporcional, tabela de partições, preparação contextual e fila. Apresenta inventário de discos com capacidade, estilo GPT/MBR, barramento e saúde.
- **Área central com rolagem** e cartões de visão geral, mapa proporcional, tabela com seleção, informações de disco e partição. Seleção no mapa sincroniza com a seleção na tabela e no painel de operações; clicar em espaço não alocado volta ao alvo disco.
- **Painel de operações contextual** apresenta todos os tipos cadastrados, desabilitando ações incompatíveis com o alvo e exibindo a razão por tooltip, sem ocultar o motivo. Formulário mostra somente parâmetros relevantes a cada tipo (sistema de arquivos, letra, tamanho, unidade de alocação, rótulo e formatação rápida). Botão de ação prepara, mas não executa.
- **Fila inferior redimensionável** com resumo de planejamento, desfazer, limpar e ação Aplicar visivelmente desabilitada enquanto o gate físico estiver fechado.
- **GridSplitters de coluna e linha** ajustam larguras de sidebar/editor e altura da fila sem abandonar inventário/contexto. Controles com estilo de foco, textos com contraste, nomes AutomationProperties e atualização via F5.
- Sem pacotes externos de UI e sem baixar fontes ou imagens: preserva os ícones e assets de branding existentes.

## Conexão com segurança existente

A nova `ContextOperationPolicy.Evaluate` é **consultiva**, baseada nas regras do `SafetyService`. Usa parâmetros hipotéticos unicamente para pré-avaliar a compatibilidade do alvo; não substitui validação de formulários reais, planejamento nem a autorização independente do executor.

- Sem disco selecionado: ações indisponíveis.
- Apenas disco selecionado: ações de partição exigem seleção de partição.
- Partição selecionada: ações de disco inteiro exigem desmarcar a partição.
- Disco 0, Windows/boot, EFI, MSR, Recovery, GUID ausente e outros alvos protegidos continuam bloqueados.
- Motivos das recusas são exibidos no painel e em tooltips acessíveis inclusive quando os botões estão desabilitados.
- `MainViewModel.QueueCommand` exige uma ação disponível para o alvo. A operação ainda passa pelo `OperationPlanner` com os **parâmetros reais**.
- `MainViewModel.ApplyCommand` é desabilitado enquanto `StorageWriteGate.ReleasedAfterSafetyAudit == false`, mas a proteção independente dentro de `ApplyAsync`, `SafeExecutionCoordinator` e `PowerShellStorageOperationExecutor` segue presente.
- Em falha de inventário, o ViewModel descarta dispositivos potencialmente obsoletos em vez de continuar exibindo resultados antigos como se fossem atuais; as operações previamente enfileiradas permanecem para revisão, mas não podem ser aplicadas.

## Validação automatizada

Testes de política contextual em `ContextOperationPolicyTests.cs` e testes de integração ViewModel/fakes em `MainViewModelUiTests.cs`: estados de seleção, alvo protegido, toolbar, mapa destacado, refresh preservando alvo, remoção de estado obsoleto e bloqueio do botão Aplicar.

O GitHub Actions compila XAML/WPF com .NET 10 em Windows e executa testes xUnit Debug e Release, publish win-x64, compilação Inno Setup e entrega de artefatos. O status exato e a contagem devem ser associados ao SHA final deste módulo. A compilação não equivale a um **smoke test interativo**; não declare navegação, drag dos splitters nem contraste conferidos por um humano antes de teste local.

## Checklist de smoke manual (pendente)

1. Iniciar o aplicativo no Windows 10/11, maximizar, restaurar e reduzir a janela ao mínimo; verificar ausência de recortes/overflow.
2. Testar o menu de navegação por mouse/teclado; tecla F5 atualiza inventário sem escrita.
3. Selecionar dois discos, um com várias partições; validar a sincronização seleção mapa/tabela/editor, inclusive espaços não alocados e dispositivo desconectado.
4. Arrastar divisórias vertical e horizontal; verificar que os conteúdos permanecem acessíveis por rolagem.
5. Verificar dicas de ação indisponível para Disco 0, EFI, MSR, Recovery e falta de GUID GPT; nenhum botão indisponível prepara operação.
6. Selecionar cada operação; confirmar campos condicionais, tamanhos e navegação por teclado.
7. Adicionar, desfazer e limpar fila; conferir que botão Aplicar continua desabilitado mesmo com ações pendentes.
8. Abrir Licença/Logs/Sobre e validar identidade visual e estados sem privilégios de administrador.

## Decisão

**Entrega de implementação UI/CI:** dependerá do pipeline final. **Gravações físicas e VHDX destrutivo: NO-GO**, preservando integralmente o C04. O smoke interativo Windows deve ser feito antes da homologação visual/comercial.
