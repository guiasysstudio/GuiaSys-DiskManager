# GDM-C01 — Integração GuiaSys Licensing (GSL-v1)

## Escopo

Implementação do cliente desktop para o contrato oficial do projeto `guiasysstudio/guiasys-licensing`, revisado na branch `fix/int08-guia-play-licensing-production`. Nenhum endpoint novo foi inventado nem o servidor alterado.

### API exata

`POST /api/v1/project/config`, `/api/v1/trial/start`, `/api/v1/trial/validate`, `/api/v1/license/activate`, `/api/v1/license/validate` e `/api/v1/license/deactivate`.
Base HTTPS: `https://licencas.guiasys.online`. Protocolo: `GSL-v1`. Identificador público do produto: `GSLI-9AFS-LE4Q-3PHZ`. O código de integração é identificador público, **não segredo de API**, segundo o contrato de integração gerado pelo Licensing.

### Segurança do cliente

- A chave pública ES256 **não** é obtida de resposta não confiável para conceder uso; somente a chave fixada em `gsl-v1-trust.json`, distribuída pelo fabricante, pode autorizar a verificação. Sem arquivo, **fail closed**.
- `gsl-v1-trust.json` (público, não secreto) deve ficar ao lado do EXE e conter `projectId`, `keyId` e `publicJwk: {kty:"EC",crv:"P-256",x:"...",y:"..."}`. Os quatro valores devem ser **copiados do painel oficial do produto** e conferidos fora de banda; jamais preencher x/y com exemplos. O C01 não tem a chave pública operacional/Project ID desse produto.
- A configuração online deve corresponder ao `projectId`, `integrationCode`, `keyId` e ponto público fixados. Troca de chave exige novo trust file aprovado/atualização autenticada, não substituição automática.
- Tokens GSL-ENT compactos ES256 são verificados com ECDsa P-256/SHA-256/P1363, assinatura exata dos dois primeiros segmentos, base64url canônico e campos vinculados a projeto, dispositivo, tipo, estado e prazo. Rejeitar `crit`/`b64` não suportados, expiração, janela offline vencida e retrocesso de relógio.
- Device ID de instalação persistente em HKCU; não derivar dos seriais de discos gerenciados. License Key e autorização cacheada cifradas via DPAPI `CurrentUser` em `%LOCALAPPDATA%\GuiaSys\DiskManager\license-state.dpapi`; sem tokens nos logs.
- A licença só é aceita após retorno positivo assinado. Offline apenas com token já válido, mesma identidade e prazo; falhas de revogação/expiração/negação online limpam a autorização antiga.
- Requests mutativos persistem o `requestId` no cache protegido antes do envio e o reutilizam ao tentar de novo a mesma operação após falha de rede. Uma nova operação lógica recebe novo ID. O servidor mantém janela de deduplicação limitada; a implementação não assume idempotência eterna.
- Interface inclui a janela **Licença**, com ativação, avaliação, validação, informações do plano/cliente e desativação. `CheckAsync` agenda revalidação online quando alcançado `validationHours` do último retorno confiável, mantendo fallback apenas se o JWS offline ainda estiver válido.
- Gate de escrita física **travado** (`StorageWriteGate.ReleasedAfterSafetyAudit=false`) até os riscos P0/P1 de C02–C04 serem resolvidos. Uma licença válida não concede permissão para alterar discos nesta reconstrução.

### Evidências e gates

A suíte usa HTTP falso com respostas de contrato e JWS ES256 assinado dinamicamente por chave de teste, sem rede real ou dados produtivos. Compilação/testes do commit final e execução humana no Windows ficam sujeitos às evidências reais do pipeline; não presumir gate aprovado só porque os arquivos foram publicados.

**Pendências para GO de integração comercial real**: obter Project ID e chave pública do produto diretamente do Licensing, empacotar trust config aprovado, testar com licença de homologação e revogação controlada e avaliar tolerância offline e reinstalação em Windows. Não efetuar ativações em produção como teste automático.
