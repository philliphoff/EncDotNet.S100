using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Viewer.Catalogs;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="S128DatasetCatalogSource"/> — verifies the mapping
/// from <see cref="S128ProductEntry"/> onto the neutral
/// <see cref="DatasetCatalogEntry"/> shape exposed by the panel.
/// </summary>
public class S128DatasetCatalogSourceTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "S128_TDS_sample.gml";

    private static S128Dataset LoadSample()
    {
        var path = Path.Combine(TestDataDir, SampleFile);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S128Dataset.Open(path);
    }

    [Fact]
    public void Empty_Source_HasNoEntries()
    {
        var src = new S128DatasetCatalogSource();
        Assert.Empty(src.Entries);
    }

    [Fact]
    public void AddDataset_PopulatesEntries_AndRaisesChanged()
    {
        var src = new S128DatasetCatalogSource();
        var raised = 0;
        src.Changed += (_, _) => raised++;

        src.AddDataset("sample.gml", LoadSample());

        Assert.Equal(1, raised);
        Assert.NotEmpty(src.Entries);
    }

    [Fact]
    public void Entries_AreNamespacedBySourceLabel()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());

        Assert.All(src.Entries, e => Assert.StartsWith("sample.gml#", e.Id));
        Assert.All(src.Entries, e => Assert.Equal(src.Id, e.SourceId));
    }

    [Fact]
    public void Entries_CarryExpectedExtendedProperties()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());

        var first = src.Entries[0];
        Assert.True(first.ExtendedProperties.ContainsKey("sourceLabel"));
        Assert.Equal("sample.gml", first.ExtendedProperties["sourceLabel"]);
        Assert.True(first.ExtendedProperties.ContainsKey("featureType"));
    }

    [Fact]
    public void RemoveDataset_RemovesEntries_AndRaisesChanged()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());
        Assert.NotEmpty(src.Entries);

        var raised = 0;
        src.Changed += (_, _) => raised++;

        var removed = src.RemoveDataset("sample.gml");

        Assert.True(removed);
        Assert.Empty(src.Entries);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RemoveDataset_UnknownLabel_IsNoOp()
    {
        var src = new S128DatasetCatalogSource();
        var raised = 0;
        src.Changed += (_, _) => raised++;

        Assert.False(src.RemoveDataset("nope.gml"));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void AddDataset_TwiceWithSameLabel_DoesNotDuplicateEntries()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());
        var initial = src.Entries.Count;

        src.AddDataset("sample.gml", LoadSample());

        Assert.Equal(initial, src.Entries.Count);
    }

    [Fact]
    public void Status_MapsToNeutralEnum()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());

        // Every neutral status value must be one of the declared enum members.
        var allowed = new[]
        {
            DatasetCatalogStatus.Unknown,
            DatasetCatalogStatus.InForce,
            DatasetCatalogStatus.Superseded,
            DatasetCatalogStatus.Withdrawn,
            DatasetCatalogStatus.Planned,
        };

        Assert.All(src.Entries, e => Assert.Contains(e.Status, allowed));
    }

    [Fact]
    public void Coverage_IsNullOrHasConsistentBoundingBox()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());

        foreach (var entry in src.Entries.Where(e => e.Coverage is not null))
        {
            var cov = entry.Coverage!;
            Assert.NotEmpty(cov.Ring);
            Assert.True(cov.MinLatitude <= cov.MaxLatitude);
            Assert.True(cov.MinLongitude <= cov.MaxLongitude);
        }
    }

    [Fact]
    public void Clear_RemovesAll_AndRaisesChanged_WhenNonEmpty()
    {
        var src = new S128DatasetCatalogSource();
        src.AddDataset("sample.gml", LoadSample());

        var raised = 0;
        src.Changed += (_, _) => raised++;

        src.Clear();

        Assert.Empty(src.Entries);
        Assert.Equal(1, raised);
    }
}
