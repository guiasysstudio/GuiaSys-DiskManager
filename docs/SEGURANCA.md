# Segurança e estado de implementação

## M01 — inventário de discos
- A aplicação executa exclusivamente os cmdlets de leitura `Get-Disk` e `Get-Partition`.
- Não existem comandos para criar, excluir, formatar, limpar ou converter discos nesta fase.
- O script é fixo e não recebe valores provenientes do usuário.
- O sistema de armazenamento do Windows determina as informações disponíveis.
- A contagem, a classificação e a interface devem ser validadas em ambiente Windows real.

## Próximas fases
Operações destrutivas exigirão identificação inequívoca do dispositivo, proteção de discos do sistema, confirmação explícita, registro persistente e testes em discos virtuais isolados.

## Status de testes
Ainda não há teste de execução do aplicativo no Windows. Revisões estáticas não substituem build, testes automatizados e validação física.
