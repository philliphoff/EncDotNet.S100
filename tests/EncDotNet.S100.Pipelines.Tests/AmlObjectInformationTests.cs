using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Object Information for AML features (#1372, design decision 13): catalogue
/// names and decoded values, AML-only attributes on standard classes marked
/// and listed last, the drawn state followed per feature, and the same names
/// and values from <c>identify_features</c> / <c>pick_features</c>. The
/// cells are synthetic.
/// </summary>
public class AmlObjectInformationTests
{
    // An LBO (AML 2.1) obstruction with two standard and three AML attributes,
    // the AML ones written first so the sort is visible, and an SBO contact.
    private static readonly SyntheticAmlCell.Options Lbo = new()
    {
        ProductSpecification = 53,
        Edition = "2.1",
        ExtraPoints =
        [
            new("OBSTRN", ("sonsig", "504"), ("CATOBS", "7"), ("magint", "12"), ("VALSOU", "41.2"), ("conlev", "0.8"), ("gendep", "")),
            new("contct", ("mulcon", "2"), ("sonsig", "503")),
        ],
    };

    private const string ObstructionRef = "10";
    private const string ContactRef = "11";

    private static void WithCell(string name, SyntheticAmlCell.Options options, Action<string> body)
    {
        var dir = Directory.CreateTempSubdirectory("aml-info-").FullName;
        try
        {
            body(SyntheticAmlCell.Write(dir, name, options));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StandardClass_NamesFromTheCatalogue_AmlAttributesMarkedAndLast() => WithCell("UKL6UT00.000", Lbo, path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);

        var info = processor.GetFeatureInfo(ObstructionRef)!;

        Assert.Equal("OBSTRN", info.FeatureType);
        Assert.Equal("Obstruction", info.FeatureTypeName);
        Assert.False(processor.Cell.IsAmlClass("OBSTRN"));
        Assert.Equal(["CATOBS", "VALSOU", "sonsig", "magint", "conlev"], info.Attributes.Select(a => a.Code));
        Assert.Equal([null, null, "AML", "AML", "AML"], info.Attributes.Select(a => a.Mark));

        var category = info.Attributes[0];
        Assert.Equal("Category of obstruction", category.Name);
        Assert.Equal("7", category.RawValue);
        Assert.Equal("Foul ground", category.DisplayValue); // "foul ground" in the catalogue

        var sonar = info.Attributes[2];
        Assert.Equal("Sonar Signal Strength", sonar.Name);
        Assert.Equal("Strong", sonar.DisplayValue);
        Assert.Equal("504", sonar.RawValue);

        // An attribute written empty ("value unknown") isn't listed.
        Assert.DoesNotContain(info.Attributes, a => a.Code == "gendep");

        // Non-enumerated values stay as encoded.
        Assert.Equal("41.2", info.Attributes[1].DisplayText);
        Assert.Equal("12", info.Attributes[3].DisplayText);
    });

    [Fact]
    public void MetaFeatures_AreNotPicked() => WithCell("UKL6UT00.000", Lbo, path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);

        // The point lies inside the M_COVR square, which a pick doesn't find.
        var hits = processor.HitTestFeatures(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude, radiusMeters: 50);

        Assert.Contains(hits, h => h.FeatureRef == ObstructionRef);
        Assert.DoesNotContain(hits, h => h.FeatureRef == SyntheticAmlCell.CoverageRecordId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(processor.Cell.IsMetaClass("M_COVR"));
        Assert.False(processor.Cell.IsMetaClass("OBSTRN"));
    });

    [Fact]
    public void AmlClass_HasNoMarks() => WithCell("UKL6UT00.000", Lbo, path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);

        var info = processor.GetFeatureInfo(ContactRef)!;

        Assert.Equal("contct", info.FeatureType);
        Assert.Equal("Contact", info.FeatureTypeName);
        Assert.True(processor.Cell.IsAmlClass("contct"));
        Assert.Equal(["mulcon", "sonsig"], info.Attributes.Select(a => a.Code));
        Assert.All(info.Attributes, a => Assert.Null(a.Mark));
        Assert.Equal("Multiple Contacts", info.Attributes[0].Name);
    });

    [Fact]
    public async Task DrawnState_FollowsTheLastPortrayal()
    {
        var dir = Directory.CreateTempSubdirectory("aml-info-").FullName;
        try
        {
            var path = SyntheticAmlCell.Write(dir, "UKL6UT00.000", Lbo);
            var notDrawn = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);
            _ = await notDrawn.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken);
            Assert.Null(notDrawn.GetFeatureInfo(ObstructionRef)!.DrawingPriority);
            Assert.False(notDrawn.IsFeatureDrawn(ObstructionRef));

            // A portrayal that draws the obstruction but not the contact.
            var drawsObstruction = new AmlDatasetProcessor(path, new DrawsOnly(ObstructionRef, priority: 18));
            Assert.False(drawsObstruction.IsFeatureDrawn(ObstructionRef));
            _ = await drawsObstruction.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken);

            Assert.Equal(18, drawsObstruction.GetFeatureInfo(ObstructionRef)!.DrawingPriority);
            Assert.True(drawsObstruction.IsFeatureDrawn(ObstructionRef));
            Assert.Null(drawsObstruction.GetFeatureInfo(ContactRef)!.DrawingPriority);
            Assert.False(drawsObstruction.IsFeatureDrawn(ContactRef));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Identify_ReturnsTheSameNamesAndValues_WithProvenance() => WithCell("UKL6UT00.000", Lbo with { SecurityMarkingCode = 507 }, path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);
        var dataset = LoadedDatasetProjector.Project(new DatasetId("aml"), processor)!;
        var service = new IdentifyFeaturesService(new Catalog([dataset]));

        var result = service.InvokeAsync(
            new IdentifyFeaturesRequest(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude),
            TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        Assert.True(result.TryGetValue(out var value));
        var obstruction = Assert.Single(value!.Features, f => f.FeatureId == ObstructionRef);
        Assert.Equal("Obstruction", obstruction.FeatureTypeName);
        var expected = processor.GetFeatureInfo(ObstructionRef)!.Attributes;
        Assert.Equal(expected.Select(a => (a.Code, a.Name, a.DisplayText, a.RawValue, a.Mark)),
            obstruction.Attributes!.Select(a => (a.Code, a.Name, (string)a.Value, a.RawValue, a.Mark)));

        var aml = obstruction.Aml!;
        Assert.Equal(processor.Info.TagText, aml.Tag);
        Assert.Equal("LBO", aml.Product);
        Assert.Equal(processor.Info.DataSetName, aml.Cell);
        Assert.Equal(processor.Info.ProtectiveMarking, aml.ProtectiveMarking);
        Assert.Equal("standard", aml.ClassKind);
        Assert.False(aml.Drawn);
        Assert.Equal(AmlCellFeatures.NotDrawnNote, aml.Note);

        Assert.DoesNotContain(value.Features, f => f.FeatureType == "M_COVR");
        var contact = Assert.Single(value.Features, f => f.FeatureId == ContactRef);
        Assert.Equal("aml", contact.Aml!.ClassKind);
        Assert.Equal("Contact", contact.FeatureTypeName);
    });

    [Fact]
    public void Identify_ReadsAmlCellsWithoutAProcessor() => WithCell("UKL6UT00.000", Lbo, path =>
    {
        LoadedDataset dataset;
        using (var stream = File.OpenRead(path))
            dataset = LoadedDatasetProjector.Project(new DatasetId("aml-s57"), "S-57", stream)!;
        var service = new IdentifyFeaturesService(new Catalog([dataset]));

        var result = service.InvokeAsync(
            new IdentifyFeaturesRequest(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude),
            TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        Assert.True(result.TryGetValue(out var value));
        var obstruction = Assert.Single(value!.Features, f => f.FeatureId == ObstructionRef);
        Assert.Equal("Obstruction", obstruction.FeatureTypeName);
        Assert.False(obstruction.Aml!.Drawn);
    });

    [Fact]
    public void Identify_LeavesOtherProductsAlone()
    {
        var match = new IdentifyMatch(new DatasetId("x"), new SpecRef("S-101", default), "1", "DepthArea", "surface", null, "inside", 0);
        Assert.Null(match.FeatureTypeName);
        Assert.Null(match.Attributes);
        Assert.Null(match.Aml);
    }

    private sealed class DrawsOnly(string featureRef, int priority) : IAmlPortrayal
    {
        public bool Draws => true;

        public async Task<VectorPortrayalResult> PortrayAsync(AmlPortrayalInput cell, RenderContext? context, CancellationToken cancellationToken)
        {
            var empty = await AmlNotDrawnPortrayal.Instance.PortrayAsync(cell, context, cancellationToken);
            return new VectorPortrayalResult
            {
                SubLayers =
                [
                    new VectorSubLayer
                    {
                        LayerKey = "aml.test",
                        LayerName = "AML test",
                        Plane = EncDotNet.S100.Interoperability.S98DisplayPlane.AdditionalMilitaryLayers,
                        Instructions = [new PointInstruction { FeatureReference = featureRef, DrawingPriority = priority }],
                    },
                ],
                Palette = empty.Palette,
                GeometryProvider = empty.GeometryProvider,
                Product = empty.Product,
                Spec = empty.Spec,
                SourceDatasetId = empty.SourceDatasetId,
                Info = empty.Info,
            };
        }
    }

    private sealed class Catalog(IReadOnlyList<LoadedDataset> datasets) : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => datasets;

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
