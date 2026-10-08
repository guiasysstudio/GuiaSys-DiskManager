# Operações

## Uso

1. Atualize o inventário e selecione um disco e, quando aplicável, uma partição.
2. Escolha a operação e preencha somente os parâmetros relevantes.
3. Adicione à fila; uma recusa do `SafetyService` é exibida antes de qualquer alteração.
4. Revise a ordem, use Desfazer/Limpar se necessário e selecione Aplicar.
5. Em ação destrutiva, confira o resumo e digite o número do disco.

O estado é consultado novamente antes de cada item. Uma falha interrompe a fila; itens não executados permanecem pendentes.

## Elevação

Leitura funciona sem administrador. Para gravação, feche e reabra o executável usando **Executar como administrador**. A aplicação não força UAC nem reinicia a si própria, evitando loops de elevação.

## Logs e erros

Use **Logs** no cabeçalho. Mensagens amigáveis aparecem na interface; stack traces e duração ficam no JSONL. Acesso negado, timeout, volume em uso, filesystem não suportado, remoção ou mudança de identidade resultam em cancelamento seguro.
