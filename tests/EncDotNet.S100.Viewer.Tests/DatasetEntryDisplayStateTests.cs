using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetEntryDisplayStateTests
{
    [Fact]
    public void Constructor_AssignsStableMapDatasetIdentity()
    {
        var entry = new DatasetEntry("/tmp/catalog/US5WA50M.000", "S-101");

        Assert.Equal("US5WA50M.000", entry.Id.Value);
    }

    [Fact]
    public void Defaults_AreVisibleAndFullyOpaque()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");

        Assert.True(entry.IsVisible);
        Assert.Equal(1.0, entry.Opacity);
        Assert.Equal(1.0, entry.RowOpacity);
    }

    [Fact]
    public void TogglingVisibility_RaisesPropertyChanged_AndDimsRow()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.IsVisible = false;

        Assert.False(entry.IsVisible);
        Assert.Equal(0.5, entry.RowOpacity);
        Assert.Contains(nameof(DatasetEntry.IsVisible), observed);
        Assert.Contains(nameof(DatasetEntry.RowOpacity), observed);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.42, 0.42)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void Opacity_ClampsToUnitInterval(double input, double expected)
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101")
        {
            Opacity = input,
        };

        Assert.Equal(expected, entry.Opacity);
    }

    [Fact]
    public void OpacityChange_RaisesPropertyChanged()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.Opacity = 0.3;

        Assert.Contains(nameof(DatasetEntry.Opacity), observed);
    }

    [Fact]
    public void LoadedEntry_ProjectsDisplayLifecycleThroughMapDataset()
    {
        var entry = new DatasetEntry("/tmp/x.h5", "S-111")
        {
            IsVisible = false,
            IsActive = true,
            Opacity = 0.6,
            AvailableTimes =
            [
                new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            ],
        };
        entry.SubLayers.Add(new DatasetSubLayer("s111.arrows", "Arrows"));
        entry.SetLoadedState(new DatasetMetadata
        {
            Spec = new SpecRef("S-111", new SpecVersion(2, 0, 0)),
        });

        entry.IsActive = false;
        entry.IsVisible = true;
        entry.Opacity = 0.4;
        entry.CurrentTime = entry.AvailableTimes[0];
        entry.SubLayers[0].IsVisible = false;
        entry.SetValidationReport(ValidationReport.Empty);
        var assessment = SpecVersionAssessment.TryCreate(
            new SpecRef("S-111", new SpecVersion(1, 0, 0)),
            [new SpecVersion(2, 0, 0)]);
        entry.SetVersionAssessment(assessment);

        var state = Assert.IsType<MapDataset>(entry.MapDataset);
        Assert.Equal(entry.Id, state.Id);
        Assert.True(state.IsVisible);
        Assert.False(state.IsActive);
        Assert.Equal(0.4, state.Opacity);
        Assert.Equal(entry.CurrentTime, state.CurrentTime);
        Assert.Single(state.AvailableTimes);
        Assert.False(Assert.Single(state.SubLayers).IsVisible);
        Assert.Same(ValidationReport.Empty, state.Validation);
        Assert.Same(assessment, state.VersionAssessment);
        Assert.True(entry.HasVersionWarning);
    }

    [Fact]
    public void RegisteredEntry_HasNoLoadedMapDataset()
    {
        var entry = new DatasetEntry("/tmp/x.000", "S-101");

        Assert.Null(entry.MapDataset);
    }
}
