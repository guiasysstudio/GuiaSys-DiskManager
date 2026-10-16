[CmdletBinding()]
param(
    [switch]$RequireCommercialGo,
    [string]$OutputPath
)

# GDM-C18: independent, read-only release evidence. Default CI mode records NO-GO
# without failing ordinary development builds. -RequireCommercialGo blocks release.
# Never sets the compiled storage gate, modifies a disk or distributes binaries.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'artifacts\readiness\commercial-readiness.json'
}
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$pg = $props.Project.PropertyGroup
$version = [string]$pg.Version
$informationalVersion = [string]$pg.InformationalVersion
$assemblyVersion = [string]$pg.AssemblyVersion
$fileVersion = [string]$pg.FileVersion
$expectedAssembly = "$version.0"
$stableVersion =
    $version -cmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z' -and
    $informationalVersion -ceq $version -and
    $assemblyVersion -ceq $expectedAssembly -and
    $fileVersion -ceq $expectedAssembly

$gateSource = Get-Content -LiteralPath (Join-Path $root 'src\GuiaSys.DiskManager\Licensing\GslV1Models.cs') -Raw
$pinSource = Get-Content -LiteralPath (Join-Path $root 'src\GuiaSys.DiskManager\Licensing\GslV1TrustLoader.cs') -Raw
# Source inspections are blockers only, never definitive authorizations:
# code may have changed outside these inspected patterns.
$writeGateExplicitlyClosed = $gateSource -cmatch 'ReleasedAfterSafetyAudit\s*=>\s*false\s*;'
$pinExplicitlyAbsent = $pinSource -cmatch 'ApprovedPublicKeyFingerprintHex\s*=\s*""\s*;'
$hasPinnedHex = $pinSource -cmatch 'ApprovedPublicKeyFingerprintHex\s*=\s*"[a-fA-F0-9]{64}"\s*;'

$portable = Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe'
$installer = Join-Path $root "artifacts\setup\GuiaSys-DiskManager-Setup-$version-x64.exe"
$signed = $false
$signatureStatus = 'Pacotes ausentes ou sem assinatura Authenticode válida.'
if ((Test-Path -LiteralPath $portable -PathType Leaf) -and
    (Test-Path -LiteralPath $installer -PathType Leaf)) {
    try {
        $one = Get-AuthenticodeSignature -LiteralPath $portable
        $two = Get-AuthenticodeSignature -LiteralPath $installer
        $signed = $one.Status -eq 'Valid' -and $two.Status -eq 'Valid' -and
            $null -ne $one.SignerCertificate -and $null -ne $two.SignerCertificate
        $signatureStatus = if ($signed) {
            'Assinaturas locais válidas; conferir identidade do publisher e cadeia em revisão externa.'
        } else {
            "Assinaturas ausentes ou inválidas (portable: $($one.Status); setup: $($two.Status))."
        }
    } catch {
        $signatureStatus = 'Assinatura não verificável neste ambiente.'
    }
}

$findings = @(
    [pscustomobject]@{
        code = 'C18-01'; passed = $false
        requirement = 'Liberação explícita e homologada do gate de escrita'
        evidence = if ($writeGateExplicitlyClosed) { 'Gate de escrita explicitamente fechado.' } else { 'Nenhuma homologação física independente fornecida; não inferir liberação por código.' }
    }
    [pscustomobject]@{
        code = 'C18-02'; passed = $false
        requirement = 'Confiança GSL-v1 em produção e fluxo ponta a ponta'
        evidence = if ($pinExplicitlyAbsent) { 'Fingerprint público de confiança ainda vazio no build.' } elseif ($hasPinnedHex) { 'Fingerprint presente, mas validação de origem e E2E não atestadas.' } else { 'Fingerprint ausente ou em formato desconhecido; validação bloqueada.' }
    }
    [pscustomobject]@{
        code = 'C18-03'; passed = [bool]$stableVersion
        requirement = 'Versionamento estável e consistente no build'
        evidence = "Version=$version, InformationalVersion=$informationalVersion, Assembly=$assemblyVersion, File=$fileVersion"
    }
    [pscustomobject]@{
        code = 'C18-04'; passed = [bool]$signed
        requirement = 'Instalador e aplicativo portátil com Authenticode válida'
        evidence = $signatureStatus
    }
    [pscustomobject]@{ code='C18-05'; passed=$false; requirement='Testes reais de instalação/upgrade/desinstalação Windows 10/11'; evidence='Sem registros de homologação executada em ambos SOs.' }
    [pscustomobject]@{ code='C18-06'; passed=$false; requirement='Gate administrativo VHDX repetido neste release'; evidence='O workflow CI não executa testes elevados em VHDX.' }
    [pscustomobject]@{ code='C18-07'; passed=$false; requirement='Aprovação independente de segurança física'; evidence='Sem teste controlado e aprovação explícita; não tocar discos físicos.' }
    [pscustomobject]@{ code='C18-08'; passed=$false; requirement='Trial, ativação, revogação e offline GSL-v1 reais'; evidence='Testes locais/sintéticos não atestam integração de produção.' }
    [pscustomobject]@{ code='C18-09'; passed=$false; requirement='Smoke UI, acessibilidade e escalas DPI'; evidence='Sem registros de teste humano em Windows 10/11, NVDA/Narrator e 100–200%.' }
    [pscustomobject]@{ code='C18-10'; passed=$false; requirement='Release pública e verificador de atualização reais'; evidence='Não há teste homologado de canal de distribuição com assinatura verificável.' }
    [pscustomobject]@{ code='C18-11'; passed=$false; requirement='Políticas de privacidade, licença de terceiros, rollback'; evidence='Auditoria jurídica/operacional e plano de recuperação pendentes.' }
    [pscustomobject]@{ code='C18-12'; passed=$false; requirement='Aprovação humana final registrada'; evidence='Nenhuma autorização humana é inferida do resultado do CI.' }
)
$blocks = @($findings | Where-Object { -not $_.passed })
$report = [ordered]@{
    schemaVersion = 1
    project = 'GuiaSys Disk Manager'
    revision = [string]$env:GITHUB_SHA
    buildVersion = $version
    auditedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    decision = if ($blocks.Count -eq 0) { 'GO' } else { 'NO-GO' }
    blockerCount = $blocks.Count
    findings = $findings
    disclaimer = 'Read-only release audit. Does not authorize storage execution, publish a release, validate installation in a real machine, or replace sign-off.'
}
$folder = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $folder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath,
    ($report | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))
Write-Host "C18 release readiness: $($report.decision), $($blocks.Count) blocker(s)."
Write-Host "Audit artifact: $OutputPath"
if ($RequireCommercialGo -and $blocks.Count -gt 0) {
    throw "Commercial distribution BLOCKED by GDM-C18 ($($blocks.Count) unresolved requirements)."
}
