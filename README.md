# GuiaSys Disk Manager

Gerenciador gráfico de discos e partições para Windows 10 e Windows 11 x64, desenvolvido pela **GuiaSys Studio** em C# 14, .NET 10, WPF e MVVM.

> Versão de desenvolvimento 0.1.0. Operações de gravação exigem administrador, passam por uma fila explícita e são revalidadas imediatamente antes da execução. Faça backup antes de alterar armazenamento.

## Recursos atuais

- inventário de discos, partições e volumes com modelo, série, barramento, saúde, GPT/MBR/RAW, flags de boot/sistema, filesystem, label, tamanho e espaço livre;
- mapa gráfico proporcional com EFI, MSR, NTFS, FAT32, exFAT, Recovery, RAW e espaço não alocado;
- fila de operações com aplicar, desfazer, limpar, resumo e confirmação reforçada;
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

## Segurança

Os testes automatizados não escrevem em hardware. O script de integração destrutiva aceita somente um VHDX temporário criado por ele próprio, valida o alvo em várias camadas e sempre tenta desmontá-lo no bloco `finally`. Não execute operações de armazenamento sem backup verificado.
