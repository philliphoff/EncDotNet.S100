using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="IDatasetProcessor.HitTestFeatures"/> is wired
/// through the real processors: the shared geometry math is unit-tested by
/// <see cref="FeatureHitTesterTests"/>; these tests prove each override feeds
/// the same feature enumeration its <see cref="IDatasetProcessor.GetFeatureInfoAt"/>
/// indexes, so a hit's <see cref="FeatureGeometryHit.Ordinal"/> round-trips to
/// the same feature, and that the default implementation is empty.
/// </summary>
public class DatasetProcessorHitTestTests
{
    private static string S411FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml");

    // The densest known real S-101 trial cell, never committed. Present only
    // locally alongside developer downloads.
    private static string DenseS101CellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
        "101GB00302045", "101GB00GB302045", "101GB00GB302045.000");

    [Fact]
    public void Gml_HitTestFeatures_RoundTripsOrdinalToFeatureInfo()
    {
        var processor = new S411DatasetProcessor(
            S411FixturePath,
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

        AssertHitsRoundTrip(processor);
    }

    [SkippableFact]
    public void S101_HitTestFeatures_RoundTripsOrdinalToFeatureInfo()
    {
        Skip.IfNot(File.Exists(DenseS101CellPath), $"Dense S-101 trial cell not present: {DenseS101CellPath}");

        var factory = new DatasetPipelineFactory(
            CreateCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());

        var processor = factory.CreateProcessor(DenseS101CellPath);

        AssertHitsRoundTrip(processor);
    }

    [Fact]
    public void HitTestFeatures_DefaultImplementation_IsEmpty()
    {
        IDatasetProcessor processor = new BareProcessor();

        Assert.Empty(processor.HitTestFeatures(0.0, 0.0, 1_000.0));
    }

    [Fact]
    public void Gml_GetFeatureGeometryAt_ResolvesHitOrdinalToDrawableGeometry()
    {
        var processor = new S411DatasetProcessor(
            S411FixturePath,
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

        Skip.If(processor.Metadata.Extent is null, "Processor did not derive an extent to aim the pick at.");
        var extent = processor.Metadata.Extent!;
        var centreLat = (extent.SouthLatitude + extent.NorthLatitude) / 2.0;
        var centreLon = (extent.WestLongitude + extent.EastLongitude) / 2.0;

        var hits = processor.HitTestFeatures(centreLat, centreLon, radiusMeters: 500_000.0);
        Assert.NotEmpty(hits);

        // Each hit's ordinal resolves to drawable geometry whose primitive
        // matches the primitive the hit was measured against.
        foreach (var hit in hits)
        {
            var geometry = processor.GetFeatureGeometryAt(hit.Ordinal);
            Assert.NotNull(geometry);
            Assert.True(geometry!.HasGeometry);
            Assert.Equal(hit.Primitive, geometry.Primitive);
        }
    }

    [Fact]
    public void GetFeatureGeometryAt_OutOfRangeOrDefault_IsNull()
    {
        IDatasetProcessor processor = new BareProcessor();

        // Default implementation (non-vector processor) returns null.
        Assert.Null(processor.GetFeatureGeometryAt(0));

        var gml = new S411DatasetProcessor(
            S411FixturePath,
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

        // Out-of-range ordinals return null rather than throwing.
        Assert.Null(gml.GetFeatureGeometryAt(-1));
        Assert.Null(gml.GetFeatureGeometryAt(int.MaxValue));
    }

    /// <summary>
    /// Hit-tests the centre of the processor's extent with a radius wide enough
    /// to catch every feature in the cell, then asserts each hit resolves back
    /// to the same feature via <see cref="IDatasetProcessor.GetFeatureInfoAt"/>.
    /// </summary>
    private static void AssertHitsRoundTrip(IDatasetProcessor processor)
    {
        Skip.If(processor.Metadata.Extent is null, "Processor did not derive an extent to aim the pick at.");
        var extent = processor.Metadata.Extent!;
        var centreLat = (extent.SouthLatitude + extent.NorthLatitude) / 2.0;
        var centreLon = (extent.WestLongitude + extent.EastLongitude) / 2.0;

        var hits = processor.HitTestFeatures(centreLat, centreLon, radiusMeters: 500_000.0);

        Assert.NotEmpty(hits);

        // Ordinals are the feature-enumeration positions: in range and distinct.
        Assert.All(hits, h => Assert.True(h.Ordinal >= 0));
        Assert.Equal(hits.Count, hits.Select(h => h.Ordinal).Distinct().Count());

        foreach (var hit in hits)
        {
            var info = processor.GetFeatureInfoAt(hit.Ordinal);
            Assert.NotNull(info);
            Assert.Equal(hit.FeatureRef, info!.FeatureRef);
            Assert.Equal(hit.FeatureType, info.FeatureType);
        }
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    /// <summary>
    /// A processor that implements only the required members and leaves every
    /// default-interface method — including <c>HitTestFeatures</c> — untouched.
    /// </summary>
    private sealed class BareProcessor : IDatasetProcessor
    {
        public SpecRef Spec => new("S-100", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
