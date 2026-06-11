using System.Collections.Generic;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetEntryVersionWarningTests
{
    [Fact]
    public void Defaults_HaveNoVersionWarning()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-111");

        Assert.False(entry.HasVersionWarning);
        Assert.Null(entry.VersionWarningTooltip);
    }

    [Fact]
    public void SetVersionAssessment_RaisesWarning_OnDivergentEdition()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-111");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        // Declared 1.0.0 but build implements 2.0.0 → MajorDivergence → warn.
        var assessment = SpecVersionAssessment.Create(
            new SpecRef("S-111", new SpecVersion(1, 0, 0)),
            [new SpecVersion(2, 0, 0)]);

        entry.SetVersionAssessment(assessment);

        Assert.True(entry.HasVersionWarning);
        Assert.False(string.IsNullOrEmpty(entry.VersionWarningTooltip));
        Assert.Contains(nameof(DatasetEntry.HasVersionWarning), observed);
        Assert.Contains(nameof(DatasetEntry.VersionWarningTooltip), observed);
    }

    [Fact]
    public void SetVersionAssessment_StaysSilent_OnMatchingEdition()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-104");

        var assessment = SpecVersionAssessment.Create(
            new SpecRef("S-104", new SpecVersion(2, 0, 0)),
            [new SpecVersion(2, 0, 0)]);

        entry.SetVersionAssessment(assessment);

        Assert.False(entry.HasVersionWarning);
        Assert.Null(entry.VersionWarningTooltip);
    }

    [Fact]
    public void SetVersionAssessment_StaysSilent_OnNewerCompatibleEdition()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-111");

        // Build implements a newer minor on the same major → info only.
        var assessment = SpecVersionAssessment.Create(
            new SpecRef("S-111", new SpecVersion(2, 0, 0)),
            [new SpecVersion(2, 1, 0)]);

        entry.SetVersionAssessment(assessment);

        Assert.False(entry.HasVersionWarning);
    }

    [Fact]
    public void SetVersionAssessment_ClearsWarning_WhenPassedNull()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-111");
        entry.SetVersionAssessment(SpecVersionAssessment.Create(
            new SpecRef("S-111", new SpecVersion(1, 0, 0)),
            [new SpecVersion(2, 0, 0)]));
        Assert.True(entry.HasVersionWarning);

        entry.SetVersionAssessment(null);

        Assert.False(entry.HasVersionWarning);
        Assert.Null(entry.VersionWarningTooltip);
    }
}
