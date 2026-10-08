# Segurança

## Garantias implementadas

- operações destrutivas são bloqueadas no Disco 0;
- alterações de disco inteiro são bloqueadas quando há Windows, boot, System, EFI, MSR, Recovery, partição oculta ou C:;
- partições protegidas não podem ser formatadas, excluídas, redimensionadas, renomeadas ou receber/remover letra;
- a identidade estável capturada na seleção deve coincidir no inventário de pré-execução e dentro do processo executor;
- apenas NTFS, FAT32 e exFAT são aceitos; letra, label, tamanho e allocation unit são validados;
- inicialização é aceita somente em disco RAW vazio; conversão destrutiva GPT/MBR não é oferecida;
- a aplicação inicia sem elevação para leitura e recusa aplicar a fila sem administrador;
- operações destrutivas exigem digitação do número do disco;
- logs não registram conteúdo de arquivos nem credenciais.

## Limites

Nenhum software elimina o risco intrínseco de manutenção de discos. Drivers podem omitir série/saúde, dispositivos podem mudar estado e falhas de energia podem interromper operações. Clonagem e recuperação avançada permanecem fora do escopo 0.1.0 porque ainda não há mecanismo transacional validado.

## Relato de vulnerabilidade

Não publique dados pessoais, serial completo de hardware ou logs sensíveis em issues públicas. Redija informações do ambiente antes de compartilhar um diagnóstico.
