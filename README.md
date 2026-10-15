# GuiaSys Disk Manager

Gerenciador gráfico de discos e partições para Windows 10 e Windows 11 x64, desenvolvido pela **GuiaSys Studio** em C# 14, .NET 10, WPF e MVVM.

> Versão de desenvolvimento 0.1.0. **Operações de gravação temporariamente bloqueadas na reconstrução** até os gates C02–C04 de identidade e segurança. Inventário continua disponível somente para leitura; nenhuma licença libera gravações nessa fase.

## Recursos atuais

- inventário de discos, partições e volumes com modelo, série, barramento, saúde, GPT/MBR/RAW, flags de boot/sistema, filesystem, label, tamanho e espaço livre;
- mapa gráfico proporcional com EFI, MSR, NTFS, FAT32, exFAT, Recovery, RAW e espaço não alocado, agora derivado da mesma geometria rigorosa do inventário;
- fila com planejamento determinístico somente leitura, detecção de conflitos, prévia de estado de metadados, efeitos em discos independentes e barreiras para geometria incerta; executar/aplicar permanece bloqueado;
- operações allow-listed: online/offline, read-only, inicialização segura de disco RAW, criação/exclusão/formatação, label, letra e redimensionamento;
- proteção central de Disco 0, C:, Windows, boot, System, EFI, MSR, Recovery e identidade alterada;
- logs JSON Lines em `%LOCALAPPDATA%\GuiaSys\DiskManager\Logs`, com retenção de 30 dias;
- publicação portátil self-contained `win-x64` e instalador Inno Setup.

Clonagem, recuperação avançada de partição e interpretação SMART proprietária não são anunciadas como disponíveis: exigem mecanismos e validação adicionais para não criar uma falsa garantia.

## Build rápido

```powershell
dotnet restore .\GuiaSys.DiskManager.slnx
dotnet build .\GuiaSys.DiskManager.slnx -c Debug
dotnet test .\GuiaSys.DiskManager.slnx -c Debug
dotnet publish .\src\GuiaSys.DiskManager\GuiaSys.DiskManager.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\portable
```

Documentação: [arquitetura](docs/ARCHITECTURE.md), [segurança](docs/SECURITY.md), [build](docs/BUILD.md), [testes](docs/TESTING.md), [operações](docs/OPERATIONS.md), [auditoria](docs/AUDIT.md) e [status verificável](docs/STATUS.md).

## Estado verificado

- 45/45 testes aprovados em Debug e 45/45 em Release, sem warnings;
- 32/32 etapas do gate administrativo VHDX aprovadas, usando somente discos virtuais descartáveis;
- CI verde para o HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`.

## Segurança

Os testes automatizados não escrevem em hardware. O gate de integração administrativa está concluído e aceita somente VHDX temporários criados pelo próprio runner, valida o alvo em várias camadas e sempre os desmonta e exclui no bloco `finally`. Não execute operações de armazenamento sem backup verificado.

## Reconstrução modular GDM-C00 → GDM-C18

A evolução para produto comercial está em etapas. A preparação C00 corrige um bloqueio de compilação, define a [arquitetura de destino](docs/ARCHITECTURE.md), documenta o [plano de licenciamento GSL-v1](docs/LICENSING-PLAN.md) e os [riscos/gates C00](docs/GDM-C00.md). **Os resultados de 45 testes e 32 etapas VHDX citados acima pertencem ao HEAD funcional de 08/10/2026** e não substituem CI do novo HEAD. O **cliente GuiaSys Licensing GSL-v1 foi implementado no GDM-C01** com UI, ativação, trial, validação online/offline ES256, cache DPAPI e desativação. **Homologação real ainda bloqueada**: faltam o Project ID e a chave pública de assinatura do produto, que precisam ser fixados no build; a segurança das gravações em discos físicos também permanece pendente de C02–C04. Consulte [GDM-C01](docs/GDM-C01.md). O [GDM-C02](docs/GDM-C02.md) implementa o núcleo de geometria e planejamento de operações, com validação de espaço livre contíguo, conflito de letras e dependências incertas, sem qualquer escrita em disco.

### C03 — identidade e segurança de operações

O núcleo de [Segurança GDM-C03](docs/GDM-C03.md) vincula a partição GPT a GUID, geometria e tipo, bloqueia alvos protegidos inclusive em chamadas diretas ao executor e exige confirmação textual envolvendo todos os discos afetados. Isso **não habilita escrita física**: o gate `StorageWriteGate` segue fechado até a conclusão dos critérios C04. Partições MBR sem GUID próprio não podem ser alteradas nesta etapa.

### GDM-C04

Journal de execução, reconciliação de operações e verificações independentes de pós-condição foram adicionados. **O gate de escrita permanece fechado**, inclusive para o runner VHDX. Consulte [GDM-C04](docs/GDM-C04.md).

### GDM-C05 — Nova interface gráfica WPF

Uma nova estrutura visual inclui sidebar de navegação funcional, painel de discos, mapa de partições proporcional, grade de volumes, painel de operações contextuais e fila inferior com altura ajustável. Ações bloqueadas exibem o motivo, entradas do formulário aparecem conforme a operação e o comando Aplicar permanece desabilitado enquanto o gate de escrita estiver fechado. Atalho **F5** atualiza o inventário em modo leitura. Nenhuma nova dependência visual externa foi adicionada. Consulte [GDM-C05](docs/GDM-C05.md) para ver os critérios de smoke visual ainda pendentes.
