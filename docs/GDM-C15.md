# GDM-C15 — Partições perdidas: comparação GPT de inventário (somente leitura)

**Data:** 15–16/10/2026 · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C14.

## Entrega

O C15 implementa um **comparador explícito de metadados GPT entre dois inventários do mesmo disco**, sem abrir handles físicos, sem ler setores brutos e sem escrever. Uma referência capturada **ANTES da perda** preserva em memória GUID, tipo GPT, posição, tamanho e flags de proteção das entradas existentes. Após **Atualizar (F5)**, o comparador confronta a referência com o novo inventário e identifica: entradas presentes e inalteradas, entradas com o mesmo GUID e geometria alterada, entradas ausentes com faixa agora não alocada, entradas ausentes com região ocupada por outras partições, e entradas protegidas ausentes.

**Não identifica partições apagadas anteriormente à captura da referência.** Uma faixa não alocada não comprova que os arquivos existem, que o NTFS está consistente ou que dados podem ser recuperados. A palavra "ausente" significa **apenas que uma antiga entrada GPT não consta da tabela observada**. Não existe varredura forense de setores, reconhecimento de boot sector, MFT, superblock, exFAT, TestDisk/PhotoRec, Partclone, restauração GPT, cópia de arquivos, backup nem motor de recuperação nesta etapa.

## Segurança e invariantes

- Captura exige ação **manual**, disco GPT online com `UniqueId` forte, tamanho/sector informado e alinhado, todas as partições GPT com GUID e tipo identificáveis e geometria sem sobreposições. Sem referência anterior não há "recuperação automática".
- O baseline mantém apenas metadados necessários, exclusivamente **em RAM**, sem persistência ou credenciais. É descartado ao mudar para outro disco, perder a unidade ou ocorrer falha de inventário.
- A comparação somente ocorre depois de outro inventário com `CapturedAt` **posterior** à referência; o relatório é invalidado após refresh ou alteração de seleção.
- Mesma unidade identificada por **UniqueId**. Número de disco pode variar, mas outro dispositivo no antigo número jamais herda a referência. Não aceita mudança de tamanho total, setor lógico, GPT/MBR/RAW ou disco offline. A geometria atual inteira deve ser consistente e partições identificáveis.
- O intervalo antigo é considerado "livre" somente se **inteiramente contido em uma única extensão não alocada**, construída via `StorageGeometry`. Sobreposição significa "conflito", não restauração sugerida.
- Partições de boot/sistema/EFI/MSR/Recovery/ocultas/especiais recebem classificação protegida, não indicação de reparo. **Disco 0 pode ser analisado somente como fonte de leitura de metadados.**
- `RecoveryReport.CanRecover` é fixo em **false**. Nenhuma operação nova na fila nem executor foi habilitado. `StorageWriteGate.ReleasedAfterSafetyAudit` permanece **false**, inclusive com licença válida.
- Não se presume compatibilidade com discos MBR, dispositivos dinâmicos, Storage Spaces, BitLocker, criptografia, snapshots, TRIM, setores remapeados ou condições da superfície.

## Fluxo WPF

Abra a seção **Partições perdidas (prévia)**. Selecione **o disco inteiro** (use Limpar seleção para sair da partição). Pressione **1 · Capturar referência** quando o disco estiver íntegro. Após uma alteração posterior, pressione **Atualizar (F5)** e **2 · Comparar após F5**. O relatório expõe classificação, posição original, tamanho, impedimentos e alertas, sem poder aplicar nada. **Descartar referência** limpa também o relatório.

A referência não é um backup e não deve ser mantida como mecanismo de proteção contra perda de dados. O software não recupera partições que já estavam perdidas quando foi iniciado ou quando a referência foi capturada.

## Testes / homologação

- `PartitionRecoveryAssessmentTests`: exigências de captura, ausência/colisão de GUID, intervalos, entrada persistente, novo GUID ocupando intervalo antigo, GUID preservado com geometria alterada, EFI/Disco 0, mudança do número do dispositivo com identidade preservada, substituição de disco, tamanho/setor/tabela alterados, timestamp inválido, metadados desconhecidos e geometria inconsistente.
- `MainViewModelUiTests`: captura somente de disco inteiro, comparação após refresh, invalidação de relatório em refresh, substituição/removal, falha de inventário, descarte e nenhuma chamada ao executor/fila.
- CI Windows: build e test Debug/Release, sintaxe PowerShell sem execução, publish win-x64 e Inno Setup. **Somente afirmar aprovação após conferir GitHub Actions do HEAD.**
- Pendente: teste visual WPF em Windows e ensaio offline/forense com imagens de disco sintéticas descartáveis. Não abrir disco com erro físico para varredura intensiva; executar preservação/imagem por ferramenta especializada.

## Limites de um futuro mecanismo de recuperação

A recuperação real demandaria **leitura setorial controlada**, análise forense de assinaturas e metadados, tolerância a I/O, cópia de imagem do dispositivo para destino seguro, análise de sobreposições, journal e rollback, verificação criptográfica, tratamento de NTFS/exFAT/FAT32/ext, BitLocker/SSD/TRIM e autorização independente em todos os níveis. Nunca restaurar entradas GPT com base apenas em uma faixa não alocada. Recuperar arquivos para **outro disco** é tipicamente mais seguro do que tentar reconstruir imediatamente a tabela de partições.

**Decisão:** C15 fornece **evidência diferencial GPT somente leitura**. **NO-GO para recuperação executável, escritas e testes destrutivos.** Próxima etapa: GDM-C16, sujeito ao planejamento modular.
