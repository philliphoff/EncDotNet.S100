using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="FeatureInformationReader"/> (#1581): a headless catalogue's
/// features read the way the viewer's Object Information reads them, with
/// catalogue names, decoded values and the catalogue's use of the type.
/// </summary>
public class FeatureInformationReaderTests
{
    private static string Dataset(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, "tests", "datasets", .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(["tests", "datasets", .. parts]);
    }

    [Fact]
    public void S101_MatchesTheViewersObjectInformation()
    {
        var path = Dataset("S101", "S-101", "DATASET_FILES", "101AA00DS0019.000");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");
        var catalog = FileDatasetCatalog.Build([new FileDatasetInput(new DatasetId("cell"), "S-101", path)]);
        var dataset = Assert.Single(catalog.Datasets);
        using var reader = new FeatureInformationReader();
        using var features = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
        var processor = new S101DatasetProcessor(path, PortrayalCatalogues(), TestLuaEngine.Create(), features);

        var compared = 0;
        foreach (var feature in FeatureAccessor.GetFeatures(dataset)!.Take(40))
        {
            var read = reader.Read(dataset, feature.Id);
            var viewer = processor.GetFeatureInfo(feature.Id);
            Assert.NotNull(read);
            Assert.NotNull(viewer);
            Assert.Equal(feature.Id, read.Feature?.Id);
            Assert.Equal(viewer.FeatureType, read.Info.FeatureType);
            Assert.Equal(viewer.FeatureTypeName ?? read.Info.FeatureTypeName, read.Info.FeatureTypeName);
            Assert.Equal(Rows(viewer.Attributes), Rows(read.Info.Attributes));
            compared++;
        }

        Assert.True(compared > 0);
    }

    [Fact]
    public void S101_NamesAreTheCataloguesAndMetaTypesSayTheirUse()
    {
        var path = Dataset("S101", "S-101", "DATASET_FILES", "101AA00DS0019.000");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");
        var catalog = FileDatasetCatalog.Build([new FileDatasetInput(new DatasetId("cell"), "S-101", path)]);
        var dataset = Assert.Single(catalog.Datasets);
        using var reader = new FeatureInformationReader();

        var all = FeatureAccessor.GetFeatures(dataset)!.Select(f => reader.Read(dataset, f.Id)!).ToList();

        Assert.All(all, f => Assert.False(string.IsNullOrEmpty(f.Info.FeatureTypeName)));
        var coverage = all.FirstOrDefault(f => f.Info.FeatureType == "DataCoverage");
        if (coverage is not null)
            Assert.Equal("meta", coverage.UseType);
        Assert.Contains(all, f => f.UseType == "geographic");
        Assert.Contains(all.SelectMany(f => f.Info.Attributes), a => a.Name is not null && a.DisplayText != a.RawValue);
    }

    [Fact]
    public void Gml_DecodesWithTheProductsCatalogue()
    {
        var path = Dataset("S124", "navwarn_point.gml");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");
        var catalog = FileDatasetCatalog.Build([new FileDatasetInput(new DatasetId("warning"), "S-124", path)]);
        var dataset = Assert.Single(catalog.Datasets);
        using var reader = new FeatureInformationReader();

        var feature = FeatureAccessor.GetFeatures(dataset)!.First();
        var read = reader.Read(dataset, feature.Id);

        Assert.NotNull(read);
        Assert.Equal(feature.Id, read.Feature?.Id);
        Assert.Equal(feature.FeatureType, read.Info.FeatureType);
        Assert.False(string.IsNullOrEmpty(read.Info.FeatureTypeName));
        Assert.NotEmpty(read.Info.Attributes);
    }

    [Fact]
    public void UnknownFeature_IsNull()
    {
        var path = Dataset("S101", "S-101", "DATASET_FILES", "101AA00DS0019.000");
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");
        var catalog = FileDatasetCatalog.Build([new FileDatasetInput(new DatasetId("cell"), "S-101", path)]);
        using var reader = new FeatureInformationReader();

        Assert.Null(reader.Read(Assert.Single(catalog.Datasets), "999999999"));
    }

    private static PortrayalCatalogueManager PortrayalCatalogues()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return manager;
    }

    private static List<string> Rows(IReadOnlyList<PickAttribute> attributes) =>
        [.. attributes.SelectMany(a => (IEnumerable<string>)[$"{a.Code}|{a.Name}|{a.DisplayText}", .. Rows(a.Children).Select(c => "  " + c)])];
}
