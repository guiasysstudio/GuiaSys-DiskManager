# Status verificável — 0.1.0-dev

## IMPLEMENTADO

- inventário, seleção sincronizada, detalhes e mapa proporcional;
- fila e operações allow-listed de disco/partição/volume;
- `SafetyService`, confirmação reforçada, revalidação e logs;
- branding, About, publish x64, instalador, CI e integração VHDX opt-in sem dependência de Hyper-V.

## TESTADO

- build e suíte xUnit: 45/45 testes aprovados em Debug e 45/45 em Release, zero warnings;
- abertura real sem elevação e inventário do Windows registrado em log;
- publicação Release self-contained `win-x64` em arquivo único;
- instalador Inno Setup 6.7.3 compilado, instalação silenciosa por usuário, abertura do app instalado e desinstalação silenciosa aprovadas.
- gate administrativo aprovado em 08/10/2026: 32/32 etapas com dois VHDX descartáveis, cobrindo GPT/MBR, online/offline, somente leitura, criação, NTFS/FAT32/exFAT, labels, letras, redução, expansão e exclusão;
- CI verde no HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`.

## VALIDADO

- regras puras de segurança, fila, parsing, classificação e identidade por testes automatizados;
- inventário somente leitura em Windows local.
- estado antes/depois de cada escrita confirmado por processos PowerShell independentes; ambos os VHDX foram desmontados e excluídos no `finally`.
- pipeline GitHub Actions reproduzindo build/test Debug e Release, publish e instalador em runner limpo.

## FORA DO ESCOPO VALIDADO

- matriz física Windows 10/11, DPI e USB descartável;
- clonagem, recuperação avançada e telemetria SMART proprietária.

Nenhuma função fora do escopo validado é apresentada na interface como suporte disponível. O gate VHDX não está pendente.
