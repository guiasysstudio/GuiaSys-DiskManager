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
- criação de partição em disco RAW e escritas em disco/partição offline ou somente leitura agora são recusadas pelo `SafetyService`;
- letra de partição ausente é normalizada para string vazia, sem caractere NUL;
- conflito de caminho/letra recebe mensagem específica, e o runner considera volumes, drives PowerShell, discos lógicos e `DriveInfo` ao escolher letras livres.
- temperatura ausente (`null`) retornada pelo Windows agora é aceita pelo parser do inventário, com teste de regressão.

## Revisão de risco

Scripts da aplicação são constantes e recebem parâmetros via JSON/stdin. Chamadas de processo usam `ArgumentList`, timeout e cancelamento. Não há `diskpart`, `clean`, escrita bruta, clonagem ou recuperação incompleta na aplicação. O runner usa `diskpart` exclusivamente para criar, anexar e desanexar VHDX por caminho controlado; as operações testadas passam pelo executor real da aplicação.

## Gates concluídos

Debug e Release compilaram sem warnings; 45/45 testes passaram em Debug e 45/45 em Release. O executável self-contained abriu, inventariou o armazenamento e permaneceu responsivo. O instalador compilou, instalou silenciosamente em escopo de usuário, abriu o aplicativo instalado e desinstalou sem resíduos do executável.

O gate administrativo VHDX de 08/10/2026 passou em 32/32 etapas. Uma primeira rodada revelou que `H:` era um drive mapeado não retornado por `Get-Volume`; a seleção de letra e a mensagem de conflito foram corrigidas, e a execução completa seguinte aprovou GPT e MBR, todos os sistemas de arquivos suportados e todo o ciclo de letras/redimensionamento/exclusão. Consultas independentes do Windows registraram o estado antes/depois. Nenhum SSD, HDD, NVMe, pendrive ou outro disco físico recebeu escrita. Os dois VHDX e seus diretórios individuais foram removidos no `finally`; a consulta final encontrou zero discos virtuais anexados.

O GitHub Actions reproduziu restore, builds, testes, publish, compilação Inno e upload dos dois artefatos em runner Windows limpo. O CI ficou verde no HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`. As actions oficiais usam versões com runtime Node.js 24. O gate VHDX está concluído, não pendente.
