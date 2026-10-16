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

O runner elevado cria dois VHDX sob uma raiz temporária exclusiva. Antes de qualquer escrita, exige disco novo, tamanho esperado, barramento virtual, identidade estável, ausência de boot/system e número diferente de zero. Cada operação é verificada por uma consulta independente. O `finally` desmonta e exclui todos os VHDX, inclusive em falha.

O gate foi concluído com 32/32 etapas aprovadas, acompanhado por 45/45 testes em Debug e 45/45 em Release. A execução aprovada em 08/10/2026 terminou com zero discos virtuais anexados, sem escrita em SSD, HDD, NVMe, pendrive ou outro disco físico. O CI ficou verde no HEAD funcional auditado `840cf2b49c1e714559e931e21b08eba74e445e7f`; não há gate VHDX pendente.

## Limites

Nenhum software elimina o risco intrínseco de manutenção de discos. Drivers podem omitir série/saúde, dispositivos podem mudar estado e falhas de energia podem interromper operações. Clonagem e recuperação avançada permanecem fora do escopo 0.1.0 porque ainda não há mecanismo transacional validado.

## Relato de vulnerabilidade

Não publique dados pessoais, serial completo de hardware ou logs sensíveis em issues públicas. Redija informações do ambiente antes de compartilhar um diagnóstico.

## GDM-C18 — Bloqueios de liberação

O `StorageWriteGate` continua **desabilitado** e `GslV1TrustLoader` não aceita trust de produção sem fingerprint público pinado no executável. **Nenhuma gravação física foi liberada.** `Test-CommercialReleaseGate.ps1` audita o build sem executar comandos de armazenamento. O JSON `commercial-readiness.json` documenta os 12 bloqueios e pendências de produção: assinatura, testes Windows reais, VHDX administrativo atualizado, licença real, segurança em mídia descartável e aprovação humana. A opção `-RequireCommercialGo` deve falhar no código de desenvolvimento. CI verde é evidência técnica de build, não autorização de release ou garantia contra perda de dados. Detalhes: [GDM-C18](GDM-C18.md).

## GDM-H02 — Chave pública de confiança não é secreta, mas exige origem autenticada

O JSON `gsl-v1-trust.json` só é aceito pelo cliente após parser rígido (8 KiB, nomes exatos, sem duplicações nem campo privado `d`, P-256 público importável) e comparação de SHA-256 canônico com pin **embutido** no executável confiável. O arquivo recebido por terceiros e seu próprio checksum **não** provam identidade do servidor. Sem pin, nenhuma ativação pode ser promovida ao estado confiável; teste unitário com chave sintética nunca autoriza produção. H02 registra `NO-GO` via script offline e mantém o bloqueio de gravação. Para aprovação, usar painel autenticado do produto + verificação fora de banda, ensaio real controlado e distribuição assinada. Ver [GDM-H02](GDM-H02.md).

## H02.1 — Âncora de chave pública GSL-v1 no build de desenvolvimento

O administrador disponibilizou o contrato público de integração, incluindo a chave EC P-256 `sig_3828131003da47bda2f7` do projeto `prj_b41909541f924fe5b837`. A aplicação confere seu SHA-256 canônico fixado em `GslV1TrustLoader` e um arquivo JSON público minimalizado, distribuído ao lado do EXE. O endpoint foi atualizado para `https://licencas.guiasys.com.br/`. Alterar apenas o JSON para outra chave **não** autoriza tokens novos; rotação exige novo build aprovado. A presença da chave pública **não comprova ativação real, validade comercial, assinatura do executável nem segurança de escrita física**; a decisão de lançamento permanece **NO-GO**. Nenhuma credencial sensível foi adicionada.
