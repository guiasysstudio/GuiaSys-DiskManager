# GDM-H03-UI — Nova organização visual inspirada no MiniTool Partition Wizard

**Escopo:** somente apresentação e organização dos controles da aplicação WPF; nenhuma alteração de API, lógica de licença, comando de disco, gate de escrita, dados de clientes ou mecanismo de atualização.

## Objetivo e referência funcional

O teste real no Windows em 16/10/2026 mostrou que o H03-01 carregou os cinco discos e nove partições, mas a tela anterior tinha três problemas de usabilidade: mapa amplo acima da tabela, barras laterais visualmente densas e pouco contraste nos atalhos. A referência de organização é o **MiniTool Partition Wizard**, sem copiar ícones, nomes de produtos, logotipos ou recursos proprietários: ações/assistentes laterais, tabela com partições, mapa visual do disco e comandos rápidos no topo.

## Mudanças aplicadas

- Tema claro com azul GuiaSys, fundo neutro, cartões brancos e texto escuro legível. Os estilos do app e dos demais diálogos agora seguem essa paleta.
- Faixa principal azul com logotipo próprio, indicativo permanente de **MODO SEGURO** e os comandos de inventário, logs, licença e sobre.
- Segunda barra de ferramentas com navegação **Partições / Mapa / Diagnóstico / Operações** e ações **Desfazer / Limpar / Aplicar**, reutilizando os comandos MVVM existentes. *Aplicar* continua desabilitado pelo `StorageWriteGate`; não foi adicionado novo caminho de gravação.
- Painel esquerdo mais estreito com atalhos legíveis e lista de discos.
- **Tabela de partições primeiro**, seguida do **mapa proporcional** do disco selecionado e do resumo geral. Assim, a tabela não fica escondida sob os cartões do dashboard.
- Mapa com segmentos menores, cores claras por tipo e legenda. Estatísticas extensas de espaço/geometria em expansor recolhido por padrão para poupar altura sem perder informação.
- Painel contextual da direita mais estreito com ações em lista vertical compacta, preservando mensagens de motivo e tooltips quando estão indisponíveis.
- Fila de operações inferior mais baixa, ainda redimensionável, com os mesmos comandos e avisos de segurança.
- Preservados `F5`, atalhos `Ctrl+1…8`, seleção de disco/partição, menus, layout responsivo por divisórias, navegação por teclado, leitura sem elevação e licenciamento GSL-v1.
- O verificador de atualizações do GitHub **não foi modificado**. Ele consulta manualmente o último release público estável, sem baixar/instalar executáveis automaticamente; isso não muda nesta etapa.

## Segurança e homologação

O tema é independente da autorização de mídia. **`StorageWriteGate.ReleasedAfterSafetyAudit` permanece `false`** e bloqueia qualquer aplicação física; a aparência de controles não constitui aprovação do executor. A revisão da interface não altera os critérios de liberação comercial C18 (NO-GO).

A verificação automática `tools/Test-DesktopUiContract.ps1` analisa o XAML da janela e o tema como XML, confirma os anchors de navegação, ordem **tabela → mapa → resumo**, atalhos/comandos já existentes, `Mode=OneWay` para indicadores somente leitura, tabela somente leitura e a trava compilada de gravação. O GitHub Actions executa esse preflight no Windows. Este teste de fonte não substitui a inspeção humana do comportamento WPF.

## Teste visual pendente no computador do desenvolvedor

Na branch `feat/full-disk-manager`, após CI verde:

```powershell
git status
git pull --ff-only origin feat/full-disk-manager
dotnet build .\GuiaSys.DiskManager.slnx -c Debug
dotnet run --project .\src\GuiaSys.DiskManager\GuiaSys.DiskManager.csproj -c Debug
```

Conferir em **100%, 125%, 150% e 200% DPI**, na janela padrão e com tamanho reduzido, se tabela, mapa, barra de ferramentas, ações, navegação, fila e diálogos continuam legíveis. O centro deve exibir a tabela antes do mapa; o inventário deve mostrar cinco discos no equipamento observado (ou a contagem atual do Windows), sem mensagens de erro. Tentar apenas seleção, navegação, atualização de inventário e abertura da janela Licença; **não executar alteração física nem usar discos reais como ensaio destrutivo**.

Se houver erro de inicialização, binding, corte/overflow, ou problemas de contraste, registrar captura de tela e o log do app, ocultando dados pessoais, seriais e licenças.

**Resultado:** reorganização de UI implementada; validação em Windows/DPI e aceite estético dependem do usuário. Lançamento comercial e escrita física seguem NO-GO.
