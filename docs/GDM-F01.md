# GDM-F01 — Interface principal profissional · F01-A

**Branch:** `feat/full-disk-manager` · **Issue:** #4 · **escopo:** apresentação WPF, sem escrita.

## Implementado nesta revisão

- Tabela de partições compacta (**172 DIP**) com botão **Mais linhas / Menos linhas**, aumentando para **320 DIP**. A tabela permanece rolável.
- Larguras mínimas por coluna, rolagem horizontal e redimensionamento manual: cabecalhos, uso, tamanho e saúde não desaparecem por compressão em janelas menores.
- Colunas ajustadas e fila mais compacta; divisórias arrastáveis preservadas.
- Navegação concentrada na lateral e nos atalhos `Ctrl+1…8`; a barra secundária não duplica o menu.
- Ações contextuais utilizam a largura disponível e mostram estado e justificativa de maneira permanente: **planejamento**, **prévia sem motor de execução** ou **ação indisponível**, sem depender de tooltip.
- Comandos Desfazer/Limpar/Aplicar ficam somente na barra superior; fila mantém a lista e o alerta de bloqueio.
- Testes xUnit adicionais para classificação das ações e verificação estática de contrato UI atualizada.

## Restrições mantidas

`StorageWriteGate.ReleasedAfterSafetyAudit=false`. Nenhuma alteração em executor, segurança, licenciamento, APIs, operações de disco ou automação de escrita. **NO-GO comercial e para gravações físicas.**

## Homologação Windows ainda necessária (F01-B)

Validar manualmente DPI 100%, 125%, 150%, 200%; múltiplos monitores; janela estreita/minimizada/maximizada; 1, 5 e muitos discos; teclado (Tab, Enter, Ctrl+1…8, F5); seleção sincronizada tabela ↔ mapa; rolagem horizontal de tabela/mapa; alternância Mais/Menos linhas; explicações visíveis no painel direito. Registrar capturas sem seriais/IDs/licenças e corrigir eventuais overflow e comportamento em Windows real.

```powershell
git status
git pull --ff-only origin feat/full-disk-manager
dotnet build .\GuiaSys.DiskManager.slnx -c Debug
dotnet run --project .\src\GuiaSys.DiskManager\GuiaSys.DiskManager.csproj -c Debug
```

A aprovação de CI Debug/Release + instalador será apurada pelo commit; isso não substitui o smoke WPF real. **Estado:** F01-A entregue em código; F01 completa aguarda F01-B.
