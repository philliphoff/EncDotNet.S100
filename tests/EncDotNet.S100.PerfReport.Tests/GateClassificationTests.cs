using EncDotNet.S100.PerfReport;
using static EncDotNet.S100.PerfReport.GateCommand;

namespace EncDotNet.S100.PerfReport.Tests;

public class GateClassificationTests
{
    private const double Threshold = 10.0;
    private const double MadK = 3.0;
    private const double RetryZoneMult = 2.0;
    private const double MinAbsolute = 100.0;

    [Fact]
    public void BelowMinAbsolute_AlwaysClean()
    {
        // Even a 1000% delta with z=100 is ignored when baseline is too small.
        var status = ClassifyScenario(1000.0, 100.0, baselineMedian: 50,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Clean, status);
    }

    [Fact]
    public void BelowThresholdPct_IsClean_RegardlessOfZ()
    {
        // 5% delta but very high z (low MAD) — still clean because pct < threshold.
        var status = ClassifyScenario(5.0, 50.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Clean, status);
    }

    [Fact]
    public void BelowMadK_IsClean_RegardlessOfPct()
    {
        // 50% delta but the noise floor (MAD) is huge → z < madK → clean.
        var status = ClassifyScenario(50.0, 1.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Clean, status);
    }

    [Fact]
    public void AboveThresholdAndMadK_BelowHardZone_IsSuspicious()
    {
        // 12% delta (>10% threshold) AND z=4 (>3.0 madK) but
        // both are < 2× their hard counterparts (20% and 6).
        var status = ClassifyScenario(12.0, 4.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Suspicious, status);
    }

    [Fact]
    public void AboveBothHardThresholds_IsRegressed()
    {
        // 25% delta and z=10 → both ≥ 2× threshold/madK → fail outright.
        var status = ClassifyScenario(25.0, 10.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Regressed, status);
    }

    [Fact]
    public void AboveHardPctButNotHardZ_IsSuspicious()
    {
        // pct passes the hard threshold (25 ≥ 20) but z does not (5 < 6).
        // Both must clear the hard zone for outright failure.
        var status = ClassifyScenario(25.0, 5.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Suspicious, status);
    }

    [Fact]
    public void RetryZoneMultOne_DisablesSuspiciousZone()
    {
        // With retry-zone-mult=1.0 the suspicious zone collapses, so
        // anything above threshold becomes a regression immediately.
        var status = ClassifyScenario(12.0, 4.0, baselineMedian: 200,
            Threshold, MadK, retryZoneMult: 1.0, MinAbsolute);
        Assert.Equal(ScenarioStatus.Regressed, status);
    }

    [Fact]
    public void NegativeDelta_IsClean()
    {
        // Improvement (faster candidate) → never regression.
        var status = ClassifyScenario(-25.0, -10.0, baselineMedian: 200,
            Threshold, MadK, RetryZoneMult, MinAbsolute);
        Assert.Equal(ScenarioStatus.Clean, status);
    }
}
