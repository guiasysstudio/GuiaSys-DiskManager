# GDM-C07 — Operações essenciais: preparação, validação e preflight

**Data:** 15/10/2026  
**Repositório:** `guiasysstudio/GuiaSys-DiskManager`  
**Branch:** `feat/full-disk-manager`  
**Base:** GDM-C06

## Objetivo

Fortalecer o fluxo de preparação de operações essenciais de disco e partição. **Esta etapa NÃO libera escrita em dispositivos** e não afirma que PowerShell de mutação tenha sido testado contra discos virtuais ou físicos. O controle `StorageWriteGate.ReleasedAfterSafetyAudit` permanece fixado em `false`.

### Ações existentes e política C07

| Grupo | Ações | Requisitos |
|---|---|---|
| Estado do disco | Online/offline; ativar/desativar somente leitura | Disco identificado, não protegido, sem partições protegidas |
| Inicialização | GPT/MBR | Somente RAW vazio e disco online/grável; **não apaga discos com dados** |
| Criação | Nova partição | **Apenas GPT** com espaço contíguo suficiente e sem partições ambíguas |
| Exclusão | Excluir partição | Alvo GPT com GUID forte, extensão/tipo idênticos aos do inventário; confirmação reforçada |
| Formatação | NTFS, FAT32, exFAT | Alvo GPT forte, unidade de alocação validada, rótulo válido; confirmação reforçada |
| Letras | Atribuir/trocar e remover letra | D–Z em maiúsculas, exclusividade no inventário, remover somente se houver letra |
| Rótulos | Alterar rótulo do volume | Comprimento máximo 32, sem caracteres de controle ou proibidos |
| Redimensionamento | Preparação para GDM-C08 | Tamanho positivo, sem invadir extensão ocupada; viabilidade real do filesystem ainda não avaliada |

**Atenção ao MBR:** o aplicativo pode **planejar a inicialização** de disco RAW vazio como MBR, mas não prepara **criação ou alteração de partições MBR**, pois o sistema de identidade de partição e a prova de pós-condição implementados no C03/C04 requerem GUID GPT. Isso é uma restrição consciente (fail-closed), não suporte completo a operações MBR.

## Validação em camadas

1. **Formulário WPF (`MainViewModel.QueueSelectedOperation`)** interpreta apenas os campos pertinentes à ação selecionada. Dados antigos em campos ocultos (ex.: tamanho de um redimensionamento anterior ou letra de outra operação) não são copiados para a nova operação. Entrada de letra exige **exatamente um caractere entre D e Z**. Tamanho obrigatório para redimensionar; opcional para criar (máximo). Tamanho de cluster considerado apenas na formatação.
2. **Contrato explícito (`EssentialOperationValidator`)** verifica alvo correto (disco inteiro ou partição), proíbe parâmetros excedentes de outra ação, exige tamanho/identidade/letra quando necessários, valida filesystem/rótulo/unidade de alocação. O próprio ViewModel chama o validador antes de incluir a operação na fila.
3. **`OperationPlanner`** confronta o novo pedido com a fila projetada sem executar escrita, e impede dependências que ficariam ambíguas após alterações de geometria.
4. **`SafetyService.Evaluate`** revalida identidade, atributos protegidos e contrato; o C07 também recusa alterações de layout ou estado integral do disco GPT se alguma partição estiver sem GUID forte ou tipo GPT confirmado, consulta geometria para confirmar a maior faixa contígua livre para criação e confere espaço adjacente antes de expansões. Recusa exclusão/letra de destinos não elegíveis.
5. **`SafeExecutionCoordinator` e executor PowerShell**, ainda fechados por `StorageWriteGate`, mantêm autorização independente, journal, refresh, confirmação e pós-condições C04. O validador de contrato não se torna uma autorização de gravação.

## Testes e evidências

- **`EssentialOperationsTests.cs`**: tipagem das 13 operações conhecidas; parâmetros não relacionados; disco vs partição; fingerprints obrigatórios; letra, tamanho, filesystem, unidade de alocação e rótulo; GUID GPT incerto; capacidade contígua disponível; MBR indeferido; disco totalmente particionado; remoção de letra ausente e gate sempre fechado.
- **`MainViewModelUiTests.cs`**: formulário ignora entradas ocultas, recusa letras de dois caracteres, não herda tamanho/letra durante formatação, recusa unidade de alocação inválida e mantém Aplicar desabilitado.
- GitHub Actions Windows: compila Debug/Release, executa xUnit, publica aplicativo win-x64 self-contained, compila Inno Setup e publica os artefatos. Relatar números de testes **somente após verificar a execução do SHA final**.

## Pendências e riscos

- A defesa PowerShell agora não trata falha de consulta das partições como disco vazio: consulta de partições GPT/MBR é feita com `-ErrorAction Stop`, e, em disco RAW, exige `NumberOfPartitions=0` explicitamente. A criação de partições exige GPT também no script. **O script de mutação ainda não foi exercitado** neste módulo: requer revisão por comando, provas de rollback/resultado indeterminado e testes em VHDX inequivocamente identificado dentro de VM descartável. **Não executar em discos reais como experimento.**
- Controles de acesso concorrente ao armazenamento entre processos, conflitos de volumes montados e validação do caminho efetivo do executável seguem pendentes de homologação.
- O preparo de redimensionamento não garante que o sistema de arquivos possa reduzir ou expandir; GDM-C08 tratará a faixa real de `Get-PartitionSupportedSize`, alinhamentos e restrições pertinentes sem alterar o gate.
- Testes visuais humanos do WPF e integração real do GSL-v1 permanecem pendentes; CI não substitui esses gates.
- Dispositivos EFI/MSR/Recovery/Windows/Disco 0 permanecem protegidos e não são alteráveis.

**Decisão de escrita:** **NO-GO** até a conclusão de todas as auditorias e testes específicos com VHDX. A finalização do C07 valida os **contratos de preparação**, não concede autorização de execução destrutiva.
