using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Pipelines.Tests;

public class MapDatasetTests
{
    [Fact]
    public void Constructor_CapturesLoadedStateWithoutRendererTypes()
    {
        var id = new MapDatasetId("US5WA50M.000");
        var extent = new BoundingBox(47.0, -123.0, 48.0, -122.0);
        var metadata = new DatasetMetadata
        {
            Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)),
            Extent = extent,
            TimeCoverage = new TimeCoverage(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc)),
        };
        var times = new List<DateTime>
        {
            new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc),
        };
        var subLayers = new List<MapDatasetSubLayer>
        {
            new("s101.areas", "Area fills", opacity: 0.75),
            new("s101.linework", "Line work", isVisible: false),
        };
        var assessment = SpecVersionAssessment.TryCreate(
            metadata.Spec,
            [new SpecVersion(1, 0, 0)]);

        var dataset = new MapDataset(
            id,
            "US5WA50M.000",
            metadata,
            isVisible: false,
            isActive: true,
            opacity: 0.5,
            availableTimes: times,
            currentTime: times[1],
            subLayers: subLayers,
            validation: ValidationReport.Empty,
            versionAssessment: assessment);

        times.Clear();
        subLayers.Clear();

        Assert.Equal(id, dataset.Id);
        Assert.Equal("US5WA50M.000", dataset.Name);
        Assert.Same(metadata, dataset.Metadata);
        Assert.Same(extent, dataset.Extent);
        Assert.False(dataset.IsVisible);
        Assert.True(dataset.IsActive);
        Assert.Equal(0.5, dataset.Opacity);
        Assert.True(dataset.HasTimeSteps);
        Assert.Equal(2, dataset.AvailableTimes.Count);
        Assert.Equal(new DateTime(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc), dataset.CurrentTime);
        Assert.Equal(2, dataset.SubLayers.Count);
        Assert.Same(ValidationReport.Empty, dataset.Validation);
        Assert.Same(assessment, dataset.VersionAssessment);
    }

    [Fact]
    public void MapDatasetId_RejectsEmptyValues()
    {
        Assert.Throws<ArgumentException>(() => new MapDatasetId(""));
        Assert.Throws<ArgumentException>(() => new MapDatasetId("   "));
    }

    [Fact]
    public void Constructor_RejectsDefaultMapDatasetId()
    {
        var metadata = new DatasetMetadata { Spec = new SpecRef("S-101", default) };

        Assert.ThrowsAny<ArgumentException>(() =>
            new MapDataset(default, "Dataset", metadata));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Constructor_RejectsInvalidOpacity(double opacity)
    {
        var metadata = new DatasetMetadata { Spec = new SpecRef("S-101", default) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapDataset(new MapDatasetId("dataset"), "Dataset", metadata, opacity: opacity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapDatasetSubLayer("layer", "Layer", opacity: opacity));
    }
}
