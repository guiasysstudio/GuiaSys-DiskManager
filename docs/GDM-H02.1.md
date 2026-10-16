# GDM-H02.1 — Conferência independente dos dados públicos de assinatura GSL-v1

**Data:** 16/10/2026 · **Estado:** verificador local implementado; provisionamento de produção **PENDENTE / NO-GO**.

## O que foi implementado

`tools/GslV1TrustInspector` é um utilitário **console .NET 10 executado localmente e sem rede**. Ele reutiliza exatamente os modelos e o parser estrito do GDM-H02 e permite conferir um documento de **chave pública** (`projectId`, `keyId`, `publicJwk` EC/P-256 `x` e `y`) contra identificadores esperados, fornecidos a partir de uma fonte autenticada e revisada independentemente.

Ele só **lê** um arquivo JSON indicado; não chama a API GSL, Firebase, GitHub, não ativa licenças, não cria nem importa chaves privadas, não modifica fonte, `ApprovedPublicKeyFingerprintHex`, instaladores ou discos. Não cria `gsl-v1-trust.json` de produção, não usa credenciais e não permite apontar um hash arbitrário como certificado de confiança.

O resultado é sempre um objeto JSON rotulado **`decision: NO-GO`** e **`provenanceApproved: false`** com o fingerprint SHA-256 **candidato** e identificadores públicos. Isso não constitui uma autorização: somente o proprietário do produto pode confirmar a origem, o vínculo projeto/assinatura, a rotação da chave e a assinatura do novo binário.

## Passo 1 — Obter identificadores OFICIAIS no Admin

Entre no **painel oficial autenticado do GuiaSys Licensing**, encontre o projeto **GuiaSys Disk Manager**, confirme o código de integração público configurado e obtenha **apenas**:

- `projectId` (padrão `prj_` + 20 caracteres hexadecimais minúsculos);
- `signing.keyId`;
- `signing.publicJwk.kty` = `EC`, `signing.publicJwk.crv` = `P-256`;
- `signing.publicJwk.x` e `signing.publicJwk.y` (coordenadas **públicas**, base64url sem `=`).

**Nunca** compartilhar `privateJwk.d`, PEM privado, token Firebase/Admin, senha, license key de cliente ou dados de clientes. Não é necessário executar `POST /api/v1/project/config` contra produção: o contrato do Licensing indica que esse endpoint pode **escrever metadados** por migração de projetos antigos. Uma resposta dessa própria API também não basta para estabelecer uma âncora de confiança independente.

Verifique a origem dos dados no Admin, registre em canal interno quem aprovou a chave, confira `projectId` e `keyId` com outra evidência administrativa independente. Se os dados forem fornecidos por terceiros, **não os considerar oficiais** sem essa revisão.

## Passo 2 — Criar arquivo TEMPORÁRIO contendo SOMENTE informação pública

Salve este modelo em um arquivo temporário fora do repositório (por exemplo, `C:\Users\<usuario>\Desktop\gsl-publico-para-conferencia.json`), substituindo todos os campos pelos dados **públicos verificados**:

```json
{
  "projectId": "<PROJECT_ID_OFICIAL>",
  "keyId": "<KEY_ID_OFICIAL>",
  "publicJwk": {
    "kty": "EC",
    "crv": "P-256",
    "x": "<COORDENADA_PUBLICA_X>",
    "y": "<COORDENADA_PUBLICA_Y>"
  }
}
```

Não comitar este arquivo ainda, não copiar placeholders para `gsl-v1-trust.json` distribuído, não incluir campo `d`, não armazenar chaves privadas. O parser exige exatamente os nomes indicados e arquivo <= 8192 bytes.

## Passo 3 — Verificar OFFLINE, localmente

Na pasta da branch `feat/full-disk-manager`, com SDK .NET 10 instalado, execute no PowerShell:

```powershell
dotnet run --project .\tools\GslV1TrustInspector\GslV1TrustInspector.csproj -c Release -- --self-test

dotnet run --project .\tools\GslV1TrustInspector\GslV1TrustInspector.csproj -c Release -- `
  --file "<CAMINHO_DO_JSON_PUBLICO>" `
  --expected-project-id "<PROJECT_ID_VERIFICADO_NO_ADMIN>" `
  --expected-key-id "<KEY_ID_VERIFICADO_NO_ADMIN>"
```

O primeiro comando utiliza apenas uma chave pública de teste gerada em memória. O segundo valida **JSON, curva/ponto ES256, campos duplicados, nomes, identidade e SHA-256 canônico**. Se houver erro, retorna código de saída diferente de zero, sem imprimir o conteúdo do arquivo. Não há conexões nem credenciais transmitidas pelo utilitário. O `dotnet run` do SDK pode restaurar dependências de desenvolvimento NuGet; a lógica do utilitário não efetua chamadas de rede.

A saída traz:

```text
"decision": "NO-GO"
"provenanceApproved": false
"candidateCanonicalFingerprintSha256": "<64 caracteres hex>"
```

**O fingerprint calculado não vira um pin de produção automaticamente.** Sua computação correta não prova que a chave veio da GuiaSys Studio.

## Passo 4 — Homologação e aprovação futuras

Somente **depois** da verificação administrativa independente, preparar commit revisado com um arquivo público confiável `gsl-v1-trust.json` e `ApprovedPublicKeyFingerprintHex` no binário. O app deve ser recompilado, assinado e testado em um ambiente de homologação com licença própria de teste. Validar trial, licença ativa, bloqueio, expiração, revogação, cache DPAPI, funcionamento offline, clock rollback, atualização/reinstalação, sem alterar o Firebase de produção durante ensaios. O fluxo de provisionamento **não foi liberado nesta etapa**.

`StorageWriteGate.ReleasedAfterSafetyAudit` **continua false** mesmo quando licenciamento for homologado: ativação de licença não autoriza alterações em discos físicos.

## Evidência e critérios

- CI executa compilação `--self-test` do inspetor com JWK efêmero e exige que entrada sem arquivo seja negada.
- A suíte existente `GslV1TrustMaterialTests` verifica campos duplicados, campos privados, ponto e base64url, fingerprint e fail-closed.
- GitHub Actions Debug/Release, checksums, Inno Setup, H01/C18 continuam obrigatórios; resultados só devem ser declarados aprovados após conferência **no commit final**.
- **PENDENTE:** `projectId`, `keyId` e coordenadas públicas oficiais do Disk Manager, conferência administrativa fora de banda, teste ponta a ponta e assinatura de código.

**Decisão:** GO apenas para criação da ferramenta de auditoria local e testes sintéticos; **NO-GO para pin de produção, licenciamento real homologado, lançamento comercial e operações físicas**.

## Atualização H02.1 — dados públicos oficiais recebidos em 16/10/2026

A GuiaSys Studio apresentou, no painel administrativo do GuiaSys Licensing, o **contrato público GSL-v1 do GuiaSys Disk Manager**, corrigindo o domínio para `https://licencas.guiasys.com.br/`. O código de integração continua `GSLI-9AFS-LE4Q-3PHZ`; o `projectId` é `prj_b41909541f924fe5b837`; a chave pública é `sig_3828131003da47bda2f7`, EC P-256/ES256. O trial estava **desativado** no contrato apresentado, mas essa é uma regra dinâmica do **servidor** e não deve ser hardcoded no desktop.

A chave **pública** foi validada localmente como ponto P-256. Seu SHA-256 canônico sobre `GSL-v1\n<projectId>\n<keyId>\n<x>\n<y>` (LF, sem LF final) é:

`6714ec5a65788f4cc937e0f2abd3656362d70ccd6d3cc09aae7e460566ef8d97`

O desenvolvimento agora inclui `Licensing/gsl-v1-trust.json` com **somente** os campos públicos mínimos (`projectId`, `keyId`, `kty`, `crv`, `x`, `y`) e esse fingerprint fixado no binário; o arquivo é copiado ao lado do EXE em build/publish Windows, e o Inno inclui a distribuição completa. Os atributos públicos opcionais de JWK `ext`/`key_ops` do contrato foram normalizados para respeitar o parser estrito já aprovado, sem alterar x/y. **Nenhuma chave privada ou licença de cliente foi incorporada.**

O CI executa testes adicionais de identidade oficial, comparação de fingerprint, rejeição de `keyId` adulterado, conferência do JSON ao lado do executável e do domínio novo. O preflight GSL continua gerando **NO-GO** pois **não foram executadas ativações/revogações em ambiente real, verificação independente do artefato assinado nem homologação humana**. O endpoint `POST /api/v1/project/config` pode fazer migração de metadados no servidor; **não foi chamado contra produção durante esta atualização**.

> Esta atualização **substitui** as afirmações históricas acima de que os identificadores públicos, fingerprint ou material JWK estão ausentes. O código já contém uma âncora pública de desenvolvimento conferida contra o contrato entregue pelo administrador; **a confiança de distribuição final e o licenciamento real continuam não homologados**, assim como a assinatura Authenticode e as gravações físicas (`StorageWriteGate=false`). Nenhuma versão foi publicada.
