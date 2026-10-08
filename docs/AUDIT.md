# Auditoria

## Origem

- `main`: somente README e política inicial.
- `feat/m01-readonly-foundation`: WPF mínimo, `Get-Disk`/`Get-Partition`, CI e receita Inno.
- `feat/m02-partition-map`: apenas filtragem das partições pelo disco selecionado.

A branch completa reutilizou a ideia segura de script fixo e substituiu a UI monolítica por MVVM, modelos ampliados, fila, executor allow-listed, segurança central, testes e documentação.

## Achados corrigidos

- ausência de testes e solution file;
- falta de identidade estável e revalidação do alvo;
- exceções técnicas expostas diretamente;
- ausência de logs, rotação, timeouts e confirmação reforçada;
- versão e branding incompletos;
- bindings WPF de propriedades somente leitura detectados e corrigidos durante teste real de abertura.

## Revisão de risco

Scripts são constantes e recebem parâmetros via JSON/stdin. Chamadas de processo usam `ArgumentList`, timeout e cancelamento. Não há `diskpart`, `clean`, escrita bruta, clonagem ou recuperação incompleta na aplicação. Operações de formato/exclusão existem somente no executor protegido e no teste VHDX isolado.
