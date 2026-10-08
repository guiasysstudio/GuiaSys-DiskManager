# Arquitetura

## Camadas

- `Models`: snapshots imutáveis de disco/partição e contratos de operação.
- `ViewModels`: estado da tela, seleção sincronizada, mapa proporcional e composição da fila.
- `Views`: WPF declarativo; code-behind limitado a diálogos e eventos estritamente visuais.
- `Services`: inventário somente leitura, execução isolada de processo, privilégios e logs.
- `Safety`: decisão central e determinística que pode negar qualquer operação.
- `Operations`: fila ordenada e executor de operações allow-listed.
- `Infrastructure`: notificação de propriedades e comandos síncronos/assíncronos.

## Fluxo de gravação

`seleção → validação SafetyService → fila → resumo/confirmar → novo inventário → validar identidade e proteções → executor → cmdlet do Windows → novo inventário`

O executor envia JSON por entrada padrão a um script PowerShell constante. Nenhum valor do usuário é concatenado em script ou linha de comando. O script possui `switch` fechado e resolve o alvo novamente por número e identidade.

## Concorrência e UI

Inventário e processos externos são assíncronos. Comandos desabilitam a ação durante execução e atualizações de coleção ocorrem na thread WPF. Processos têm timeout, cancelamento e encerramento da árvore em cancelamento.
