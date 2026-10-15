# GDM-C04 — Execução controlada, journal, pós-condições e reconciliação

**Data:** 15/10/2026. **Repositório:** `guiasysstudio/GuiaSys-DiskManager`. **Branch:** `feat/full-disk-manager`. **Base:** GDM-C03.

## Alcance

Este módulo fortalece o caminho de execução **sem ativar a gravação real em discos físicos ou virtuais**. As condições de aprovação para escrita comercial ainda não foram integralmente satisfeitas. `StorageWriteGate.ReleasedAfterSafetyAudit` permanece **false** e o runner VHDX recusa execução antes de criar/anexar discos virtuais.

### Fluxo implementado

`Solicitud → gate fechado (sem escrita)` é o fluxo **em vigor**. Para liberação futura, o executor já contém o pipeline:

1. Serialização por `SemaphoreSlim` global **apenas dentro do processo do aplicativo**. Não equivale a lock do Windows entre processos ou lock de volume.
2. Verificação de journal anterior: qualquer tentativa sem reconciliação, ou com resultado indeterminado, bloqueia uma nova operação. Journal inconsistente/ilegível causa recusa.
3. Autorização interna do executor: administrador, licença GSL-v1 válida, inventário atual e `SafetyService` conservador, além da chave de liberação física.
4. Journal `Prepared` e `Dispatching` persistido/flush antes de lançar PowerShell. Se a gravação do journal falhar, não iniciar o comando.
5. Script PowerShell fixo e allow-listed, com validação adicional de disco/partição C03. **Nenhum teste com gravação foi realizado nesta etapa.**
6. Um novo inventário independente após um retorno bem-sucedido do processo; `OperationPostconditionVerifier` compara estado antes/depois.
7. Journal `Reconciled` com resultado `Verified` ou `Indeterminate`; a interface só considera concluída a operação cujo efeito foi realmente comprovado.

### Semântica de resultado

- **Rejected**: nenhum comando de escrita foi despachado (gate fechado, inventário inválido, permissão negada, exceção anterior ao dispatch).
- **Verified**: processo retornou confirmação de saída e o inventário subsequente comprovou o efeito exigido, sem alteração inesperada de identidade/topologia.
- **Indeterminate**: erro, timeout, cancelamento ou falha de consulta/journal após início **possível** do dispatch; não é seguro assumir sucesso, fracasso nem rollback. A fila WPF é esvaziada para impedir clique de reexecução. O registro permanece exigindo reconciliação humana.

O process runner passou a aplicar timeout também à escrita em stdin; matar subprocesso após timeout **não** desfaz alteração já iniciada.

### Journal local

Arquivo padrão: `%LOCALAPPDATA%\GuiaSys\DiskManager\Journal\operations.jsonl`. JSONL append-only na lógica, com `FileOptions.WriteThrough` e `Flush(true)` em cada entrada. Contém IDs de tentativa/operação, números de disco/partição, tipo, fase, estado, timestamp UTC e código fechado. **Não** inclui número de série, chave de licença, conteúdo de arquivos, rótulos de volumes ou saídas arbitrárias do PowerShell.

**Limite:** o arquivo pertence ao usuário atual e não é assinado nem resistente à adulteração por administradores/processos maliciosos. O journal é evidência para diagnóstico e quarentena operacional, não uma prova criptográfica ou um mecanismo de transação ACID.

### Pós-condições cobertas

- Online/offline e somente leitura: flags solicitadas presentes, sem alteração de topologia.
- Inicialização GPT/MBR: estilo final correto a partir de disco RAW vazio, sem partições inesperadas.
- Criação: exatamente uma partição nova com GUID forte e tamanho requerido quando explícito, preservando as anteriores.
- Exclusão: alvo não existe mais; demais partições preservadas.
- Formatação, letra, remoção de letra e label: valor solicitado presente em partição cuja identidade/topologia foi preservada.
- Redimensionamento: GUID, posição e tipo mantidos, tamanho final exato e demais partições preservadas.
- Outras transições de estilo de disco ou identidade do dispositivo causam resultado indeterminado.

O verificador não presume que a ausência de um erro ou o exit code 0 seja evidência suficiente. Possíveis diferenças de alinhamento podem levar a resultado conservador `Indeterminate` e exigir conferência humana.

### Testes e VHDX

- `StorageExecutionTests.cs`: testes isolados de resultados, mudanças de partições inesperadas, identidade, journal de preparado/dispatch/confirmado, entradas truncadas, inconsistência de tentativa e privacidade dos registros.
- O xUnit e o GitHub Actions não exigem elevação nem escrevem em disco físico. A compilação Windows x64/instalador está coberta pelo pipeline usual.
- `tests/GuiaSys.DiskManager.IntegrationRunner/Program.cs` retorna código 2 **antes de criar qualquer VHDX** enquanto o gate de escrita estiver fechado.
- O runner legado ganhou retorno de erro quando o cleanup não for concluído; anteriormente `return 0` no `try` podia mascarar erro detectado no `finally`.
- **Não foi executado teste VHDX destrutivo neste C04.** O runner ainda precisa de revisão completa para o novo esquema de autorização, ensaio em VM descartável e evidência de identidade de alvo antes de ser liberado.

### Pendências de aprovação (NO-GO)

1. Coordenação/lock **entre processos**, bloqueio de volume em uso, proteção da origem do executável em mounts não triviais, e segurança contra mudanças pelo Windows no intervalo entre verificação e comando (TOCTOU residual).
2. Auditoria de todos os comandos PowerShell, incluindo falhas de consulta `Get-Partition`, validações de limites do filesystem, geometria/alinhamento e risco de volume montado.
3. Teste destrutivo apenas em VHDX gerado e identificado inequivocamente numa VM descartável com permissão administrativa, incluindo restart/queda de energia simulada e cleanup.
4. Testes manuais da interface, confirmação de múltiplos discos, elevação e operação de leitura em ambientes Windows 10/11.
5. Homologação real do GSL-v1 do Disk Manager (public key fingerprint/ProjectId ainda pendentes de configuração na assinatura).
6. Política humana/documentada de desbloqueio do gate; **não** converter automaticamente a conclusão deste módulo em `true`.

**Decisão:** implementação do pipeline de reconciliação/journal e testes automatizados: pronta para CI. **Permissão para gravar discos comerciais: NO-GO.**
