# Testes

## Automatizados

O projeto xUnit cobre classificação, tamanhos, parser, identidade, fila, ordem/desfazer, formatos aceitos e recusas do `SafetyService`. São 45 testes; não dependem de hardware nem gravam em disco.

## Integração VHDX

`tests/integration/Test-VhdxOperations.ps1` executa o runner .NET em modo Release. Ele é destrutivo apenas dentro dos dois VHDX temporários que cria e não depende de Hyper-V. Requer administrador e `diskpart`, valida caminho, tamanho, barramento, identidade, Disco 0, boot e system antes de cada sequência de escrita.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\Test-VhdxOperations.ps1
```

O relatório JSON registra estado antes/depois por consultas independentes. O cenário cobre GPT e MBR; online/offline e somente leitura; criação; NTFS, FAT32 e exFAT; label; atribuição, troca e remoção de letra; redução, expansão e exclusão; além dos bloqueios do `SafetyService`.

O CI não executa esse teste por depender de elevação administrativa. Testes finais em Windows 10, Windows 11 e hardware USB descartável continuam gates manuais e não foram necessários para aprovar a integração VHDX.
