# GDM-H01 — Pré-homologação Windows sem execução de manutenção em discos

**Data:** 16/10/2026 · **Branch:** `feat/full-disk-manager` · **Origem:** GDM-C18 (lançamento comercial NO-GO).

## Objetivo e limite

A etapa H01 acrescenta a **verificação reproduzível da distribuição Windows x64** e prepara os testes humanos da interface e do instalador. O teste automatizado **não instala, não inicia o aplicativo, não usa PowerShell de armazenamento, não solicita elevação nem examina discos/partições**. Apenas lê executáveis, manifesto SHA-256, versões de arquivos, status da assinatura e dados gerais da plataforma Windows; escreve somente um relatório JSON.

**H01 não conclui a homologação e não altera o NO-GO comercial ou o bloqueio físico do C18.** Executáveis `0.1.0-dev` podem ser gerados para teste de engenharia, mas não devem ser vendidos nem classificados como software pronto para gerir partições reais.

## Automação implementada

O comando `tools/Test-WindowsReadOnlyPreflight.ps1` exige:

- Windows x64; arquivos reais `GuiaSys.DiskManager.exe` e `GuiaSys-DiskManager-Setup-X.Y.Z-x64.exe`, com nomes iguais aos artefatos esperados na versão `Directory.Build.props`.
- Tamanho mínimo plausível de ambos (> 1 MiB). Versões de arquivo Windows devem corresponder a `X.Y.Z` informado.
- `SHA256SUMS.txt` com **exatamente duas entradas**, somente nomes esperados, hashes hexadecimais minúsculos e nenhuma duplicata. Hash SHA-256 local de cada arquivo precisa coincidir.
- `Get-AuthenticodeSignature` consulta a assinatura dos dois pacotes sem executá-los. A falta de assinatura é registrada no relatório e não autoriza comercialização. A opção `-RequireValidSignatures` faz a pré-verificação falhar se alguma assinatura não for válida, mas a validade local **não comprova a identidade do publisher por si só**.

O relatório `artifacts/smoke/windows-readonly-preflight.json` inclui versão de build, status SHA-256, versões, status de assinatura, arquitetura, build do Windows e declarações explícitas de que **não houve instalação nem testes humanos**. Não coleta seriais, rótulos de volumes, licenças, endereços de arquivos pessoais ou conteúdo de dispositivos de armazenamento. Só escreve o arquivo de evidência de teste.

No CI Windows, a etapa H01 roda **após** gerar e validar os artefatos. Uma segunda chamada usa um **manifesto de hash intencionalmente adulterado em diretório de artefatos**, sem modificar os binários reais, e confirma que é recusado. O workflow publica `GuiaSys-DiskManager-H01-readonly-preflight`.

## Executar a pré-verificação local (somente leitura)

Execute em Windows x64 dentro de uma cópia do repositório **na branch de desenvolvimento** e extraia os três artefatos do **mesmo run/commit** nos diretórios esperados:

```powershell
cd <PASTA_DO_GUIASYS_DISK_MANAGER>
git switch feat/full-disk-manager

# Coloque os arquivos extraídos:
# .\artifacts\portable\GuiaSys.DiskManager.exe
# .\artifacts\setup\GuiaSys-DiskManager-Setup-0.1.0-x64.exe
# .\artifacts\checksums\SHA256SUMS.txt

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-WindowsReadOnlyPreflight.ps1
Get-Content .\artifacts\smoke\windows-readonly-preflight.json
```

Essa verificação **não inicia o programa** nem executa o instalador. A flag `-ExecutionPolicy Bypass` vale somente para o processo PowerShell indicado e não faz qualquer operação de particionamento; consulte o script antes de executar em máquina de terceiros.

Para pacotes extraídos em locais diferentes, forneça `-PortablePath`, `-InstallerPath`, `-ManifestPath` e `-OutputPath`. Nunca reutilize checksum de um commit diferente. **SHA-256 anexado junto do executável não autentica o fornecedor**: verifique o SHA em um canal independente/autenticado e exija assinatura reconhecida antes de distribuir comercialmente.

## Homologação humana pendente (Windows 10 e Windows 11)

Use preferencialmente **VMs descartáveis**, snapshots e contas de teste. Somente iniciar o aplicativo e navegar: **não selecionar Aplicar, não autorizar operações destrutivas, não montar discos físicos de clientes para ensaios, não validar gravações reais**. A autenticação do GuiaSys Licensing permanece não configurada para o build comercial.

| Passo | Ação do testador | Resultado exigido | Estado |
|---|---|---|---|
| H01-UI-01 | Abrir portátil em conta não administrativa | Interface abre ou exibe falha de inventário legível sem encerrar incorretamente | PENDENTE |
| H01-UI-02 | Inventário e mapa com discos virtuais comuns | Tamanho, unidades e espaço não alocado plausíveis, sem alterações no hardware | PENDENTE |
| H01-UI-03 | Teclado Ctrl+1…8, Tab, Enter e Espaço | Navegação, foco e seleção de partição seguros | PENDENTE |
| H01-UI-04 | Saúde, manutenção e recuperação | Somente diagnóstico/prévia; não iniciar CHKDSK, TRIM nem recuperação | PENDENTE |
| H01-UI-05 | Selecionar unidade protegida e examinar fila | Motivo de segurança explícito; botão Aplicar permanece bloqueado | PENDENTE |
| H01-UI-06 | F5, desconectar VHDX descartável | Atualização clara e descarte de prévias obsoletas, sem escrita | PENDENTE |
| H01-UI-07 | DPI 100/125/150/200%, 1366x768 e 1920x1080 | Layout navegável e textos/botões acessíveis, sem cortes bloqueantes | PENDENTE |
| H01-UI-08 | Narrator e NVDA, mouse/teclado | Nomes, ordem de Tab, anúncio de erros e estado de foco compreensíveis | PENDENTE |
| H01-LIC-01 | Abrir tela de licença sem credenciais | Não configurar nem contornar o GSL; falha de trust não dá acesso a escrita | PENDENTE |
| H01-UPD-01 | Verificar atualização com/sem rede | Mensagens claras; não baixar ou instalar automaticamente | PENDENTE |
| H01-INS-01 | Instalar em VM, encerrar app e atualizar na VM | UAC previsível, ícones corretos, sem execução silenciosa de operações de disco | PENDENTE |
| H01-INS-02 | Desinstalar em VM, comparar dados do perfil | Sem remoção não autorizada de documentos/licença/logs, sem arquivos de app órfãos inesperados | PENDENTE |

Em **cada sistema operacional**, registrar versão/build, versão do pacote e SHA-256, resultado `APROVADO/FALHOU/NÃO TESTADO`, descrição reproduzível e referência da evidência (captura/log **sem dados sensíveis**). O resultado de Win11 no GitHub Actions **não substitui** o teste do aplicativo nem uma instalação real em Windows 10.

## Critérios de passagem para H02

- CI do HEAD H01 aprovado (restore, Debug/Release, testes, publish, Inno, SHA-256, pré-verificação, negativo de hash alterado e C18 NO-GO).
- Sem relatórios de falha **não investigados** nos testes humanos. O trabalho não pode alegar aprovação de UI antes de receber os resultados efetivos.
- O C18 continuará NO-GO até GSL real verificado, assinatura, testes em Windows 10/11, VHDX elevado isolado e aprovação formal por responsável humano.
- Nenhuma alteração na branch `main`, nenhuma release ou tag de produto e nenhuma alteração do bloqueio global `StorageWriteGate`.

**Próxima etapa:** H02 — configuração criptográfica de confiança GSL-v1 e homologação do licenciamento em ambiente real, mediante obtenção dos identificadores e JWK **públicos** confiáveis, sem compartilhar chaves privadas ou senhas.
