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

## Gates concluídos

Debug e Release compilaram sem warnings; 41 testes passaram em ambas as configurações. O executável self-contained abriu, inventariou o armazenamento e permaneceu responsivo. O instalador compilou, instalou silenciosamente em escopo de usuário, abriu o aplicativo instalado e desinstalou sem resíduos do executável. O teste VHDX permaneceu corretamente pendente porque a sessão não tem elevação nem cmdlets Hyper-V.
