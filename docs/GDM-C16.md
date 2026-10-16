# GDM-C16 — Acessibilidade, navegação por teclado e mapa estável

**Data:** 16/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C15.

## Objetivo

Corrigir o risco de inacessibilidade do mapa proporcional do disco apontado no C00 e facilitar a navegação entre os painéis WPF. A etapa é **somente de apresentação e usabilidade**: não adiciona comandos de disco, não altera executores, filas, permissões ou licenças. O `StorageWriteGate` permanece fechado.

## Mudanças

- Os segmentos do mapa eram `Border` com evento exclusivo de mouse. Passaram a ser **botões WPF nativos**, que podem receber foco e acionamento com **Tab + Enter/Espaço**, além de clique. Isso inclui os intervalos não alocados (que limpam a partição selecionada e voltam ao disco inteiro).
- Cada botão usa o mesmo modelo `PartitionSegmentViewModel` com **nome acessível**, posição inicial em bytes, tamanho real, tipo, indicação de seleção e ajuda textual (`AutomationProperties.Name`/`HelpText`).
- A seleção agora **atualiza IsSelected por notificação** `INotifyPropertyChanged`, sem remover/recriar as instâncias dos segmentos. A árvore WPF não perde os botões focados quando o usuário escolhe uma partição. Troca de disco e `F5` ainda recompõem o inventário e o mapa intencionalmente.
- Estilo do botão conserva as cores de tipo, destaque de item selecionado e uma borda visível ao foco via teclado. Continua havendo escala **aproximada** e largura mínima para partições pequenas; número de bytes no tooltip é exato, não calculado pela largura.
- Adicionados atalhos **Ctrl+1 a Ctrl+8**: visão geral, mapa, partições, saúde, partições perdidas, manutenção, editor e fila, respectivamente. A função de mapeamento `NavigationShortcuts.Resolve` só aceita combinação exata **Control + dígito** (incluindo teclado numérico); não intercepta digitação comum, Alt/Shift ou F5.
- Sidebar indica os atalhos. Ao navegar, a interface rola até o painel e tenta transferir foco para um ponto identificável (seção, tabela ou editor). Seções e área de status ganharam rótulos de automação; status usa indicação de região viva `Polite`. **Isso não certifica compatibilidade com todos os leitores de tela** nem emissão de eventos UIA, que dependem de teste manual.
- Grade de partições permanece **somente leitura**, com seleção ligada ao ViewModel. Ação de seleção não gera operação nova nem altera um disco.

## Testes automatizados

- `MainViewModelUiTests`: confirma que selecionar e limpar partição **mantém as mesmas instâncias** dos segmentos, sincroniza o estado e não modifica a fila.
- `MainViewModelUiTests`: nome acessível com offset e tamanho, item selecionado, evento `PropertyChanged` e intervalo não alocado explícito.
- `NavigationShortcutsTests`: Ctrl+1…8 e variantes de teclado numérico; rejeição de outras teclas/com modificadores.
- Pipeline Windows compila o XAML real (`MainWindow.xaml`) em Debug/Release, executa suíte xUnit nas duas configurações, publica app win-x64 e gera instalador Inno. **Resultado sempre vinculado ao último SHA**, não aos módulos anteriores.

## Homologação humana pendente

1. Abrir no Windows 10 e 11; com teclado apenas, Ctrl+2 vai ao mapa. **Tab** avança pelos segmentos; **Enter/Espaço** seleciona; o foco deve continuar no mesmo item.
2. Confirmar seleção de intervalo não alocado, destaque, limpar seleção, tabela e editor sem criar operações.
3. Alterar disco, desconectar VHDX de teste, executar F5; UI não deve conservar resultados antigos como atuais. F5 não escreve.
4. Conferir zoom do Windows 100%, 125%, 150% e 200%; janela mínima, divisórias, região rolável e legendas não devem ocultar funções.
5. Testar leitores de tela (NVDA, Narrator) com nomes de itens, estados, foco, ordem de Tab, explicações de botões desabilitados e status. `AutomationProperties.LiveSetting` não é garantia de anúncio sem teste do evento UI Automation.
6. Revisar contraste e distinção de estados sem depender apenas de cor; testar mouse, teclado numérico e teclados sem numpad.

**Decisão:** GO para implementação de acessibilidade e navegação submetida à compilação/testes; **NO-GO para alegar WCAG homologado**, deploy comercial ou qualquer escrita física enquanto os gates específicos não forem concluídos.
