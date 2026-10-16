# GDM-H02 — Pré-homologação de confiança e licenciamento GSL-v1

**Data:** 16/10/2026 · **Branch:** `feat/full-disk-manager` · **Estado:** engenharia de segurança aprovada somente após CI; **produção e integração real ainda NO-GO**.

## Objetivo

Preparar com segurança a validação do GuiaSys Licensing, cujo endpoint público do contrato é `https://licencas.guiasys.online/` e o código **identificador público** do produto já consta em `GslV1Protocol`. Nenhuma ativação de cliente, criação de licença, leitura de dados privados, acesso Firebase, deploy, alteração em produção ou operação em disco foi feita em H02. **Não há trust pin de produção nem licença de teste real fornecida**; logo, não é tecnicamente honesto declarar a integração homologada.

### Proteções implementadas

- `GslV1TrustMaterial.ParseStrict`: parser puro de documento **público** de configuração `gsl-v1-trust.json`, limitado a 8.192 bytes UTF-8, JSON estrito sem comentários/trailing commas, chaves obrigatórias **exatas**, sem duplicações (inclusive `projectId` e `publicJwk.x`), campos desconhecidos ou chave privada `d`. Suporta só JWK público `kty=EC, crv=P-256` com coordenadas base64url canônicas e ponto importável no P-256. Falhas são `InvalidDataException`.
- `GslV1TrustMaterial.FingerprintHex`: digest determinístico de 32 bytes SHA-256 minúsculo sobre exatamente `GSL-v1\n<projectId>\n<keyId>\n<x>\n<y>`, UTF-8, separadores LF e **sem LF final**. A ordem e capitalização são parte do protocolo.
- `GslV1TrustMaterial.MatchesPinnedFingerprint`: rejeita digest vazio, tamanhos/hex incorretos e diferenças com comparação de tempo constante. A forma maiúscula do mesmo digest é permitida; **nunca** promove `hash` gerado a confiança oficial.
- `GslV1TrustLoader.Load`: só lê o arquivo após confirmar que o executável tem um fingerprint público fixado pelo publicador; limita a leitura e exige tanto parser rígido quanto coincidência com o fingerprint embutido. **A constante `ApprovedPublicKeyFingerprintHex` continua vazia**; portanto, nenhuma chave de arquivo, servidor, usuário ou teste pode habilitar o cliente distribuído nesta revisão.
- Testes `GslV1TrustMaterialTests` com chaves efêmeras **sintéticas**: estrutura JWK, digest/canonicalização, troca de `kid`/projeto, campo duplicado, chave privada indevida, curva incorreta, malformação e arquivo substituído; preservam `StorageWriteGate=false`.
- `tools/Test-GslV1TrustReadiness.ps1` **não chama nenhuma API** nem lê licenças/chaves privadas; inspeciona apenas presença de contrato/pin no código e arquivo público na pasta portátil. Gera `artifacts/licensing/gsl-v1-readiness.json`, explicitamente `NO-GO`. Se detectar `gsl-v1-trust.json` empacotado sem pin, alteração do write gate ou contrato público incompatível, falha. O modo `-RequireProductionTrust` falha até que haja homologação independente real — não aceita variável de ambiente que simule aprovação.
- CI Windows: executa suíte Debug/Release e preflight H02 em modo **offline**, comprova que a liberação de produção é recusada, publica `GuiaSys-DiskManager-H02-GSL-NO-GO`.

## Contrato do documento público

O arquivo distribuído **somente após verificação do administrador**, ao lado do EXE (não em `%APPDATA%`):

```json
{
  "projectId": "<PROJECT_ID_REAL_DO_DISK_MANAGER>",
  "keyId": "<KEY_ID_PUBLICO_DA_ASSINATURA>",
  "publicJwk": {
    "kty": "EC",
    "crv": "P-256",
    "x": "<X_PUBLICO_BASE64URL>",
    "y": "<Y_PUBLICO_BASE64URL>"
  }
}
```

**Isso é um esquema, não um arquivo executável/configurado.** Nunca inserir placeholders no instalador. `projectId` segue `prj_` + 20 hex minúsculos, `keyId` contém somente `[A-Za-z0-9_-]`, e x/y são coordenadas públicas de 32 bytes em base64url **sem padding**.

**Atenção:** as coordenadas JWK são públicas, mas a origem precisa ser verificada **fora da banda**. Não confiar apenas na mesma resposta HTTPS usada para verificar licenças, no arquivo JSON recebido por chat ou em digest calculado de documento não verificado. Um invasor que controle ambos ainda produzirá SHA-256 válido de sua própria chave; o hash é uma âncora **somente depois de aprovação independente** do proprietário.

## Procedimento H02.1 — dados oficiais ainda necessários

1. No **painel oficial GuiaSys Licensing**, abrir o projeto exato do **GuiaSys Disk Manager** e conferir o código de integração configurado. Confirmar se projeto/planos realmente estão habilitados para ensaio e sua política de licença/avaliação.
2. Obter pelo painel/admin confiável os **valores públicos**: `projectId`, `signing.keyId`, `signing.publicJwk.kty`, `crv`, `x`, `y`. **Nunca** usar `d`, PEM privado, token de serviço, chave de licença de cliente ou credenciais de Firebase.
3. Um responsável autorizado deve conferir a origem por **canal independente** (painel autenticado, mudanças de chaves auditadas, identidade do projeto) antes de calcular o digest. Revisar o resultado do parser estrito e comparar o hash canônico SHA-256. Não obter a âncora exclusivamente do endpoint que será verificado.
4. Somente após aprovação, criar pacote público assinado que inclua `gsl-v1-trust.json`, preencher o `ApprovedPublicKeyFingerprintHex` com digest de 64 hex conferido e **recompilar/reassinar** executável e instalador. **A instalação não confia em edição posterior de JSON**. Rotação do JWK requer uma nova aprovação e uma atualização autenticada.
5. Homologar com **licença própria de teste**, preferencialmente em ambiente controlado autorizado, sem anexar a chave aos logs/repo/chat. Exercitar ativação, revalidação, expiração, revogação, dispositivo vinculado, trial, desativação, reincidência de `requestId`, cache DPAPI CurrentUser, mudança de relógio e indisponibilidade da rede. Confirmar que ao receber revogação online a autorização offline fica inválida.
6. Registrar evidências desidentificadas de sucesso e falhas, revisão de privacidade, tempo offline do servidor e comportamento na reinstalação. Mesmo licença ativa **não autoriza** escrita em disco; `StorageWriteGate.ReleasedAfterSafetyAudit=false` até auditoria física independente.

### Checklist de ensaios reais — todos ainda PENDENTES

| Gate | Critério | Estado |
| --- | --- | --- |
| H02-01 | Project ID e JWK assinante conferidos no painel e fora da banda | PENDENTE |
| H02-02 | SHA-256 canônico aprovado e pin incorporado ao binário autenticado | PENDENTE |
| H02-03 | `gsl-v1-trust.json` público validado e incluído no instalador assinado | PENDENTE |
| H02-04 | Ativação trial e licença reais em dispositivo de homologação | PENDENTE |
| H02-05 | Revogação/suspensão/desativação efetivas e estado offline | PENDENTE |
| H02-06 | Prazo offline, expiração, troca de relógio, perda de rede e cache DPAPI | PENDENTE |
| H02-07 | Atualização/desinstalação, outro usuário Windows, troca de dispositivo | PENDENTE |
| H02-08 | Evidência humana redigida sem dados confidenciais e vinculada ao SHA | PENDENTE |

### Fluxo local de engenharia (não conecta ao Licensing)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-GslV1TrustReadiness.ps1
Get-Content .\artifacts\licensing\gsl-v1-readiness.json

# Deve falhar na versão de engenharia:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-GslV1TrustReadiness.ps1 -RequireProductionTrust
```

O `NO-GO` é esperado e correto neste estado. Ver também [GDM-C01](GDM-C01.md), [GDM-C18](GDM-C18.md) e [GDM-H01](GDM-H01.md).

## Decisão

**GO para validações sintéticas e endurecimento do parser ES256, condicionado ao CI no HEAD. NO-GO para ativação real homologada, publicação comercial, confiança de produção e qualquer execução de gravação.** A etapa seguinte depende da conferência do Project ID e do JWK **públicos oficiais** em canal administrativo confiável e da homologação humana H01 já documentada.
