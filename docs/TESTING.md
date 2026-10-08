# Testes

## Automatizados

O projeto xUnit cobre classificação, tamanhos, parser, identidade, fila, ordem/desfazer, formatos aceitos e recusas do `SafetyService`. Não depende de hardware nem grava em disco.

## Integração VHDX

`tests/integration/Test-VhdxOperations.ps1` é destrutivo apenas dentro do VHDX temporário que ele cria. Requer administrador e os cmdlets Hyper-V `New-VHD`, `Mount-VHD` e `Dismount-VHD`. O script valida caminho, tamanho, barramento, Disco 0, boot e system antes de inicializar.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\Test-VhdxOperations.ps1
```

O CI não executa esse teste por depender de elevação/Hyper-V. Testes finais em Windows 10, Windows 11 e hardware USB descartável continuam gates manuais.
