using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Release;

namespace GuiaSys.DiskManager.Tests;

public sealed class ReleaseReadinessAssessmentTests
{
    private static ReleaseVerificationEvidence AllEvidence() => new(
        SignedPortableAndInstaller: true,
        Win10AndWin11InstallUpgradeUninstallTested: true,
        VirtualDiskRunnerRevalidatedForThisRelease: true,
        PhysicalDiskSafetyApproved: true,
        ProductionLicensingEndToEndValidated: true,
        AccessibilityAndDpiSmokeTested: true,
        PublicReleaseUpdateChannelTested: true,
        DistributionAndRollbackApproved: true,
        HumanReleaseSignoff: true);

    [Fact]
    public void Current_development_build_is_explicitly_no_go()
    {
        var report = ReleaseReadinessAssessment.Evaluate(new(), "0.1.0-dev");
        Assert.False(report.CommercialGo);
        Assert.Equal(12, report.Findings.Count);
        Assert.Equal(report.Findings.Count - 1, report.BlockerCount);
        Assert.StartsWith("NO-GO", report.Decision);
    }

    [Fact]
    public void Even_all_manually_claimed_evidence_cannot_bypass_compiled_security_gates()
    {
        var report = ReleaseReadinessAssessment.Evaluate(AllEvidence(), "1.0.0");
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
        Assert.True(GslV1TrustLoader.HasPinnedTrustFingerprint);
        Assert.False(report.CommercialGo);
        Assert.Equal(1, report.BlockerCount);
        Assert.Contains(report.Findings, x => x.Code == "C18-01" && !x.Passed);
        Assert.Contains(report.Findings, x => x.Code == "C18-02" && x.Passed);
    }

    [Theory]
    [InlineData("0.1.0-dev")]
    [InlineData("0.1")]
    [InlineData("v0.1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.2.3-preview")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3/evil")]
    [InlineData("")]
    public void Invalid_commercial_version_is_reported_not_silently_coerced(string version)
    {
        var report = ReleaseReadinessAssessment.Evaluate(AllEvidence(), version);
        Assert.Contains(report.Findings, x => x.Code == "C18-03" && !x.Passed);
        Assert.False(report.CommercialGo);
    }

    [Theory]
    [InlineData("0.9.1")]
    [InlineData("1.0.0")]
    [InlineData("12.35.478")]
    public void Three_component_stable_versions_are_valid_format_but_not_release_permission(string version)
    {
        var report = ReleaseReadinessAssessment.Evaluate(AllEvidence(), version);
        Assert.Contains(report.Findings, x => x.Code == "C18-03" && x.Passed);
        Assert.False(report.CommercialGo);
    }

    [Fact]
    public void Null_evidence_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ReleaseReadinessAssessment.Evaluate(null!, "1.0.0"));
    }

    [Fact]
    public void Release_findings_have_unique_codes_and_actionable_explanations()
    {
        var result = ReleaseReadinessAssessment.Evaluate(new(), "0.1.0-dev");
        Assert.Equal(result.Findings.Count, result.Findings.Select(x => x.Code).Distinct().Count());
        Assert.All(result.Findings, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.EvidenceRequired));
            Assert.False(string.IsNullOrWhiteSpace(x.Requirement));
            Assert.Equal(x.Code == "C18-02" ? "PASS" : "BLOCKED", x.Status);
        });
    }

    [Fact]
    public void A_valid_license_never_releases_the_physical_storage_gate()
    {
        var allowed = new GslLicenseDecision(true, GslLicenseStatus.ActiveLicense, "synthetic");
        Assert.False(StorageWriteGate.Allows(allowed));
        Assert.False(ReleaseReadinessAssessment.Evaluate(new(), "1.0.0").CommercialGo);
    }
}
