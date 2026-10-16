# Testes — GuiaSys Disk Manager

## Testes automatizados (sem escrita em disco)

Na branch `feat/full-disk-manager`, a solução .NET 10 executa testes xUnit em Windows via GitHub Actions nos modos **Debug e Release**. O pipeline verifica restore, compilação (0 warnings/erros na validação anterior), testes, publish self-contained Windows x64, compilação Inno Setup e upload de artefatos. A contagem exata e a aprovação do HEAD devem ser lidas nos logs da execução atual; **não se deve reutilizar um CI verde de SHA diferente**.

Os testes incluem inventário/parsing e geometria, segurança de GUID GPT e tipos protegidos, licença GSL-v1, fila/planejador e, a partir do C04, `OperationPostconditionVerifier` e `OperationJournal`. O journal usa arquivos temporários de teste; nenhum teste xUnit faz operações destrutivas ou exige privilégios administrativos.

## Runner VHDX (bloqueado)

O runner legado `tests/integration/Test-VhdxOperations.ps1` cria VHDX GPT/MBR e utiliza comandos destrutivos somente em virtualização. Essa rotina **não deve ser executada enquanto o gate de gravação estiver fechado**. O `Program.Main` verifica `StorageWriteGate.ReleasedAfterSafetyAudit` e retorna status 2 antes de criar, anexar ou alterar qualquer VHDX. O teste depende de admin/Windows e não roda no CI.

O runner foi desenvolvido antes da política de identidade de partição e licenciamento do C03. Ele **não está adaptado** para executar escrita de forma autorizada; é um artefato legado para revisão, não prova de liberação do C04.

### Roteiro de homologação futura (não executado)

1. Revisar e auditar integralmente o runner, inclusive script de cleanup e distinção entre disco novo/anexado/dispositivo previamente existente.
2. Validar caminho temporário único, tamanho, `UniqueId`, barramento virtual, flags de boot/sistema, GUID GPT de partição e número do disco; abortar diante de qualquer ambiguidade.
3. Executar em **VM Windows descartável**, como administrador, somente após autorização explícita e política de teste exclusiva. Nunca utilizar discos reais do usuário.
4. Cobrir interrupção durante formatação/redimensionamento, status indeterminado, journal e tentativas de reexecução: operações não podem ser repetidas automaticamente.
5. Confirmar pós-condições por inventário independente, garantir desmontagem/correta exclusão apenas do VHDX criado, auditar falhas de cleanup e retornar código de saída não zero.
6. Validar UI, confirmações, licenciamento real e comportamentos sem administrador em Windows 10/11.

**Gate de escrita física/comercial: NO-GO até auditoria manual e evidência documentada.** Ver [GDM-C04](GDM-C04.md).
