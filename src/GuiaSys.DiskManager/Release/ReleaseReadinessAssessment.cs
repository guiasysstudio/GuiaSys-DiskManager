using GuiaSys.DiskManager.Licensing;

namespace GuiaSys.DiskManager.Release;

/// <summary>
/// Advisory C18 readiness assessment. This is NOT a production authorization token.
/// Evidence requires independent human review; booleans alone cannot prove real tests.
/// Safety and licensing gates are read from the compiled application, not passed by UI.
/// </summary>
public sealed record ReleaseVerificationEvidence(
    bool SignedPortableAndInstaller = false,
    bool Win10AndWin11InstallUpgradeUninstallTested = false,
    bool VirtualDiskRunnerRevalidatedForThisRelease = false,
    bool PhysicalDiskSafetyApproved = false,
    bool ProductionLicensingEndToEndValidated = false,
    bool PublisherPublicSigningKeyAndPackageApproved = false,
    bool AccessibilityAndDpiSmokeTested = false,
    bool PublicReleaseUpdateChannelTested = false,
    bool DistributionAndRollbackApproved = false,
    bool HumanReleaseSignoff = false);

public sealed record ReleaseGateFinding(
    string Code, string Requirement, bool Passed, string EvidenceRequired)
{
    public string Status => Passed ? "PASS" : "BLOCKED";
}

public sealed record ReleaseReadinessReport(
    string Target, IReadOnlyList<ReleaseGateFinding> Findings)
{
    public bool CommercialGo => Findings.Count > 0 && Findings.All(x => x.Passed);
    public int BlockerCount => Findings.Count(x => !x.Passed);
    public string Decision => CommercialGo ? "GO — exige aprovação externa documentada" :
        $"NO-GO: {BlockerCount} requisito(s) de liberação não cumprido(s)";
}

public static class ReleaseReadinessAssessment
{
    public static ReleaseReadinessReport Evaluate(
        ReleaseVerificationEvidence evidence, string? version)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        // InformationalVersion is required to be a strict three-part stable SemVer.
        // Development suffixes such as 0.1.0-dev fail closed; no arbitrary 1.0 minimum.
        var stableVersion = version is not null &&
            System.Text.RegularExpressions.Regex.IsMatch(version,
                @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)) &&
            System.Version.TryParse(version, out _);
        var gate = new List<ReleaseGateFinding>
        {
            new("C18-01", "Bloqueio de escrita liberado mediante auditoria de segurança",
                StorageWriteGate.ReleasedAfterSafetyAudit,
                "Auditoria independente de executor/TOCTOU com VHDX e hardware de descarte controlado."),
            new("C18-02", "Chave pública GSL-v1 fixada e pacote/procedência homologados",
                GslV1TrustLoader.HasPinnedTrustFingerprint &&
                evidence.PublisherPublicSigningKeyAndPackageApproved,
                "Public key pin local não é homologação: confirmar origem por canal independente, pacote assinado e revisão de deploy."),
            new("C18-03", "Versão comercial estável e revisada",
                stableVersion,
                "Version/AssemblyVersion/FileVersion/InformationalVersion alinhados com tag estável."),
            new("C18-04", "Executáveis Authenticode assinados e cadeia conferida",
                evidence.SignedPortableAndInstaller,
                "WinVerifyTrust/Get-AuthenticodeSignature para portátil e instalador finais."),
            new("C18-05", "Instalação, atualização, downgrade e desinstalação homologados",
                evidence.Win10AndWin11InstallUpgradeUninstallTested,
                "Windows 10/11 x64 em perfis com/sem admin; licença/logs preservados."),
            new("C18-06", "Runner VHDX administrativo seguro revisado nesta versão",
                evidence.VirtualDiskRunnerRevalidatedForThisRelease,
                "Runner elevado opt-in, VHDX descartável e limpeza validada sem tocar em discos físicos."),
            new("C18-07", "Segurança de escrita em discos físicos formalmente homologada",
                evidence.PhysicalDiskSafetyApproved,
                "Aprovação explícita, backup e evidências de teste em mídia descartável."),
            new("C18-08", "Licença real GSL-v1, revogação e falhas de rede testadas",
                evidence.ProductionLicensingEndToEndValidated,
                "Trial/ativação/renovação/revogação e cache com ambiente real."),
            new("C18-09", "Acessibilidade, DPI e navegação validados visualmente",
                evidence.AccessibilityAndDpiSmokeTested,
                "NVDA/Narrator, 100/125/150/200% em Windows 10 e 11."),
            new("C18-10", "Canal público de atualizações validado",
                evidence.PublicReleaseUpdateChannelTested,
                "Release estável com instalador nomeado corretamente, checksum e browser."),
            new("C18-11", "Política de distribuição, rollback e privacidade aprovada",
                evidence.DistributionAndRollbackApproved,
                "Assinatura, SmartScreen, LGPD, licenças de terceiros, recuperação e plano de rollback."),
            new("C18-12", "Aprovação humana registrada para comercialização",
                evidence.HumanReleaseSignoff,
                "Checklist assinado e revisão de evidências independentes do CI.")
        };
        return new("GuiaSys Disk Manager " + (version ?? "versão desconhecida"), gate.AsReadOnly());
    }
}
