using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Core.Tests.DynamicSources;

public class DynamicFeatureRecordTests
{
    [Fact]
    public void DynamicFeature_RequiredFields_ProduceUsableRecord()
    {
        var t = DateTimeOffset.UtcNow;
        var f = new DynamicFeature
        {
            Id = "ownship",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (47.6, -122.3) },
            LastUpdated = t,
        };

        Assert.Equal("ownship", f.Id);
        Assert.Null(f.Kind);
        Assert.Null(f.Motion);
        Assert.Empty(f.Attributes);
        Assert.Equal(t, f.LastUpdated);
    }

    [Fact]
    public void DynamicSourceMetadata_RendererKeyOptional()
    {
        var m = new DynamicSourceMetadata { DisplayName = "Own Ship" };
        Assert.Equal("Own Ship", m.DisplayName);
        Assert.Null(m.RendererKey);
    }

    [Fact]
    public void DynamicFeaturesChanged_DefaultChangedIdsEmpty()
    {
        var c = new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset };
        Assert.Empty(c.ChangedIds);
    }
}
