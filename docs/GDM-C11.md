# GDM-C11 — Clonagem de discos e partições: prévia somente leitura

**Data:** 15/10/2026 · **Projeto:** GuiaSys Disk Manager · **Branch:** `feat/full-disk-manager` · **Base:** GDM-C10

## Escopo entregue

GDM-C11 implementa um **diagnóstico conservador de compatibilidade geométrica de clonagem** entre dois dispositivos, com modo de disco inteiro e modo de partição individual. Não inclui um mecanismo de cópia de blocos, snapshot VSS, criação ou ajuste de metadados de boot/GPT/MBR, rotina de recuperação, transferência incremental ou verificação criptográfica dos dados copiados. **Nenhum byte foi copiado ou escrito pelo C11.**

`ClonePreviewAssessment.Analyze(snapshot, sourceDiskNumber, targetDiskNumber, sourcePartitionNumber?)` produz um `ClonePreviewReport`. O relatório pode indicar **geometria preliminar compatível**, mas `CanExecute` é **incondicionalmente `false`**, independentemente do resultado. Não existe `StorageOperationType.CloneDisk` nem `ClonePartition`, e o executor PowerShell mantém allowlist que não inclui clonagem.

## Análise comum (origem e destino)

- Fonte e destino devem ser dispositivos **distintos**, com `UniqueId` forte e diferente. O inventário completo passa pelo `StorageGeometry.Build`: recusa números e identidades duplicados, GUIDs malformados e extents sobrepostos ou fora do disco.
- **Disco 0**, sistema, boot, EFI, MSR, Recovery, partições ocultas e tipos especiais recebem bloqueio no estudo; não se avalia clonagem de Windows a quente como se fosse uma simples transferência de bytes.
- Ambos online; destino não somente leitura. Origem pode estar somente leitura porque não precisa ser modificada.
- Verifica setores lógicos potências de dois, entre 512 e 65.536 B, e requer igualdade entre origem/destino para um estudo de cópia setorial direta. Recusa capacidade não alinhada; diferença ou desconhecimento de setores físicos gera alerta de risco.
- Origem ou destino removidos, identidade insuficiente, inventário ambíguo, destino igual à origem e insuficiência de capacidade são recusados.
- O relatório registra a identidade dos dois discos, carimbo de captura, bytes que **precisariam ser lidos**, impedimentos e advertências. Não presume que espaço vazio no sistema de arquivos equivale a menos bytes para copiar.

## Modo: clonar disco inteiro

- Origem deve apresentar tabela GPT/MBR e ao menos uma partição. RAW vazio não é elegível a uma “clonagem de dados”.
- Destino deve estar **RAW completamente vazio**, com capacidade **igual ou maior que a capacidade física integral da origem**.
- O volume estimado corresponde à capacidade total da fonte, **incluindo espaço não alocado**. Não se reduz a transferência usando `VolumeAvailableBytes` ou `Get-Volume`.
- Discos GPT com partições sem identidade/tipo forte e layouts com partições especiais/boot são recusados.
- Persistem riscos de colisão de IDs, GUIDs e assinaturas, GPT secundária, boot UEFI/Legacy, GPT sobre discos maiores, sistemas de arquivos e volumes que mudam durante a cópia. Esses riscos não são resolvidos por este diagnóstico.

## Modo: clonar partição

- Exige partição **básica GPT com GUID forte** e sistema de arquivos conhecido `NTFS`, `FAT32` ou `exFAT`. MBR ou tipo GPT especial é recusado.
- O destino deve estar previamente inicializado em **GPT**. Pode conter outras partições de dados verificáveis, desde que não sejam protegidas.
- Busca uma **única extensão não alocada** suficiente para a capacidade **integral da partição** e alinhada a **1 MiB**. Reserva margem conservadora de 1 MiB nas duas extremidades do disco para evitar confusão com metadados GPT. O offset escolhido é apenas sugestão de prévia; nenhuma partição é criada.
- Não faz cópia de sistema de arquivos, não traduz GUID, não estima transferência somente dos arquivos ocupados e não garante alinhamento/compatibilidade para qualquer dispositivo.
- Uma extensão agregada de espaço livre não substitui **uma faixa contígua**: se nenhuma extensão inteira comportar a origem, o diagnóstico recusa.

## Interface WPF

Novo cartão **C11 · Clonagem (somente prévia)** no painel de operações: o disco selecionado no inventário é a origem; selecionar uma partição muda o estudo para escopo de partição; um `ComboBox` separado exige que o usuário indique explicitamente **outro disco de destino**. O botão **Analisar clonagem · sem gravar** nunca prepara uma mutação.

- A seleção de destino não é assumida automaticamente.
- Ao atualizar o inventário, a prévia é invalidada. O destino selecionado é restaurado **somente se a mesma identidade forte continuar presente**; se desaparecer, é limpo, sem substituir por outro disco.
- Trocar a origem, partição ou destino invalida o relatório antigo.
- Falha na leitura do inventário remove alvos e relatório, mantendo a aplicação em estado seguro.
- As mensagens da UI deixam claro que nenhuma leitura de conteúdo de arquivos, cópia ou gravação é feita. A rotina lê **apenas o inventário de armazenamento**.

## Testes e evidência

- `ClonePreviewAssessmentTests.cs`: origens GPT/MBR, destino RAW vazio, capacidade integral vs espaço utilizado, setores lógicos 512/4096 e inválidos, diferenças físicas, Disco 0, origem/destino iguais ou com UniqueId duplicado, proteção EFI/Recovery, origem offline/destino somente leitura, clonagem de partições GPT, offsets exatos, falta de extensão contígua, filesystem desconhecido, setor incompatível, geometria sobreposta, ausência de código de clonagem na allowlist e gate global fechado.
- `MainViewModelUiTests.cs`: seleção explícita do destino, modos disco/partição, recusa de destino RAW para partição, invalidação após mudança de seleção/inventário e nunca enfileirar cópia.
- CI Windows: validação AST não executante dos 2 scripts PowerShell, compilação Debug/Release, xUnit, publicação win-x64 self-contained, criação e upload do instalador Inno Setup e portátil. Somente declarar aprovação após executar e verificar **SHA final**.

## Gate e próximas auditorias

`StorageWriteGate.ReleasedAfterSafetyAudit => false` permanece intacto. Este módulo não inclui qualquer caso de uso ou comando de escrita. Não foi realizado teste destrutivo em VHDX, teste com discos físicos, clonagem real, teste visual humano da WPF ou validação de boot após clone.

Para implementar clonagem real serão necessários: mecanismo de cópia com proteção contra seleção reversa, locks e VSS/volumes a frio, audit trail com hashes por bloco/segmento e journal persistente, tratamento de bad blocks, cancelamento e falha de energia, preservação/regeração de IDs de disco e GPT quando aplicável, validação de filesystem e boot, política de rollback e laboratório isolado em VHDX descartáveis. A aprovação do cálculo geométrico **não** resolve nenhum desses requisitos.

**Decisão GDM-C11:** **GO apenas para prévia e documentação; NO-GO para clonagem executável ou produção destrutiva.** Próximo planejado: **GDM-C12 — Migração do Windows**, sempre sem liberar escrita antes de auditoria dedicada.
