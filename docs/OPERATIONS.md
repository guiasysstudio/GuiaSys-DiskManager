# Operações

## Uso

1. Atualize o inventário e selecione um disco e, quando aplicável, uma partição.
2. Escolha a operação e preencha somente os parâmetros relevantes. O núcleo C02 calcula espaço livre contíguo e planeja estados previsíveis, sem gravar.
3. Adicione à fila. O planejador reavalia o novo item sobre a fila anterior; riscos e conflitos são apresentados antes de aceitar. Mudanças de layout exigem novo inventário antes de enfileirar dependentes no mesmo disco.
4. Revise a ordem, resumo e aviso de dependências. Use Desfazer/Limpar. **Aplicar não executa operações nesta fase: o gate de gravação permanece fechado até C02–C04 de segurança concluídos.**
5. Após liberação futura, confirmações destrutivas serão revisadas para vincular *todos* os discos envolvidos; não considere o diálogo legado seguro para uso físico.

A implementação legada prevê nova consulta por item, mas ainda não estabelece revalidação autoritativa da identidade de partição no executor; o uso para escrita em discos físicos **não está aprovado**. Para o planejamento atual, apenas metadados cuja transição é conhecida são projetados; alterações de layout e filesystem exigem observação real.

## Elevação

Leitura funciona sem administrador. Para gravação, feche e reabra o executável usando **Executar como administrador**. A aplicação não força UAC nem reinicia a si própria, evitando loops de elevação.

## Logs e erros

Use **Logs** no cabeçalho. Mensagens amigáveis aparecem na interface; stack traces e duração ficam no JSONL. Acesso negado, timeout, volume em uso, filesystem não suportado, remoção ou mudança de identidade resultam em cancelamento seguro.
