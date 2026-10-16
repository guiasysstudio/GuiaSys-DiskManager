# GuiaSys Licensing — plano e implementação do GDM-C01

O planejamento do GDM-C00 foi concretizado em um cliente GSL-v1 durante o GDM-C01. A documentação técnica atual, inclusive o bloqueio de produção por falta de confiança pública fixada e os testes, está em [GDM-C01](GDM-C01.md).

## Identidade e fronteira

Produto comercial: **GuiaSys Disk Manager**; protocolo solicitado: **GSL-v1**; integração gerida no **GuiaSys Licensing**. O identificador/código de integração e credenciais operacionais não devem ser escritos em logs, comitados como segredo nem embutidos em arquivos públicos por conveniência. A auditoria do contrato oficial GSL-v1 confirmou que o `integrationCode` é identificador público, não segredo. Project ID e JWK público são necessários para fixação criptográfica; chave privada jamais é distribuída.

A interface `ILicenseEntitlements` e a implementação `GslV1LicensingService` foram criadas, sem compartilhar autorização com `IStorageExecutor`. A biblioteca de licenciamento não poderá executar scripts PowerShell, comandos de partição ou assumir elevação administrativa.

## Comportamentos-alvo para validação em C01

1. Separar estados `Unknown`, `Valid`, `Expired`, `Revoked`, `DeviceMismatch`, `NetworkUnavailable` e `ServerError`; mapear exatamente os códigos GSL-v1 reais antes de implementar.
2. Definir operações liberadas por entitlement a partir do contrato verdadeiro; leitura de inventário e acesso ao diagnóstico seguro não devem ser confundidos com autorização de escrita.
3. Revalidar direito de uso no momento da execução, sem tornar o bloqueio de segurança do disco dependente da resposta de licença.
4. Validar respostas e assinaturas/tokens pelo protocolo oficial; não inventar endpoints, chaves, payloads ou períodos offline. Falha de verificação é fail-closed para recursos comerciais protegidos.
5. Se houver cache offline autorizado, exigir integridade, prazo e vinculação a dispositivo conforme servidor; lidar com relógio adulterado, revogação e reinstalação.
6. Armazenar segredos/sessão somente em mecanismo protegido da plataforma (avaliar DPAPI ou Windows Credential Manager); nunca persistir token íntegro em log ou telemetria.
7. Cobrir expiração, revogação, troca de dispositivo, indisponibilidade de rede, timeouts e replay com testes usando serviços falsos; incluir contrato real no gate C01.
8. Não compartilhar serial completo dos discos nem topologia de armazenamento na API de licença sem necessidade explícita e avaliação de privacidade.

## Critério de liberação

Antes de habilitar funcionalidades pagas: API GSL-v1 comprovada, fluxo real de ativação/desativação validado, licença de teste aprovada, comportamento offline definido, testes de falha e documentação de suporte. O C00 não executou esses gates. No C01, testes HTTP simulados e verificação criptográfica foram implementados; testes reais e validação de publicação ainda estão pendentes.
