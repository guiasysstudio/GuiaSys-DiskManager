# Segurança

## Garantias implementadas

- operações destrutivas são bloqueadas no Disco 0;
- alterações de disco inteiro são bloqueadas quando há Windows, boot, System, EFI, MSR, Recovery, partição oculta ou C:;
- partições protegidas não podem ser formatadas, excluídas, redimensionadas, renomeadas ou receber/remover letra;
- a identidade estável capturada na seleção deve coincidir no inventário de pré-execução e dentro do processo executor;
- apenas NTFS, FAT32 e exFAT são aceitos; letra, label, tamanho e allocation unit são validados;
- inicialização é aceita somente em disco RAW vazio; conversão destrutiva GPT/MBR não é oferecida;
- criação exige disco inicializado, online e gravável; operações de partição exigem disco e partição online e graváveis;
- a aplicação inicia sem elevação para leitura e recusa aplicar a fila sem administrador;
- operações destrutivas exigem digitação do número do disco;
- logs não registram conteúdo de arquivos nem credenciais.

## Gate VHDX

O runner elevado cria dois VHDX sob uma raiz temporária exclusiva. Antes de qualquer escrita, exige disco novo, tamanho esperado, barramento virtual, identidade estável, ausência de boot/system e número diferente de zero. Cada operação é verificada por uma consulta independente. O `finally` desmonta e exclui todos os VHDX, inclusive em falha. A execução aprovada em 08/10/2026 terminou com zero discos virtuais anexados.

## Limites

Nenhum software elimina o risco intrínseco de manutenção de discos. Drivers podem omitir série/saúde, dispositivos podem mudar estado e falhas de energia podem interromper operações. Clonagem e recuperação avançada permanecem fora do escopo 0.1.0 porque ainda não há mecanismo transacional validado.

## Relato de vulnerabilidade

Não publique dados pessoais, serial completo de hardware ou logs sensíveis em issues públicas. Redija informações do ambiente antes de compartilhar um diagnóstico.
