using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// An S-57 object whose class has no translation rule is drawn as "?" and can
/// be picked (S-52; S-64 3.2.1; issue #1242). The cell is the committed NOAA
/// fixture with a point, a line and an area re-classed to 500, 501 and 502.
/// </summary>
public sealed class S57DatasetProcessorUnknownObjectTests : IDisposable
{
    private const string QuestionMark = "QUESMRK1";

    private readonly string _directory = Directory.CreateTempSubdirectory("s57-unknown-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private S57DatasetProcessor CreateProcessor()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S57DatasetProcessor(
            SyntheticS57Cell.WriteWithUnknownClasses(_directory, "US5MA1BO.000"),
            catalogues,
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    private static string ObjectClass(FeatureInfo info) =>
        info.Attributes.Single(a => a.Code == S57UnknownObject.ObjectClassAttribute).RawValue;

    [Fact]
    public async Task UnknownClasses_DrawAQuestionMarkForTheirPrimitive()
    {
        var processor = CreateProcessor();
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.OtherInformation } },
            TestContext.Current.CancellationToken);
        var instructions = result.SubLayers.SelectMany(l => l.Instructions).ToList();

        string? ClassOf(DrawingInstruction i) =>
            processor.GetFeatureInfo(i.FeatureReference) is { FeatureType: S57UnknownObject.FeatureTypeCode } info
                ? ObjectClass(info)
                : null;

        // The S-101 Default rule: SY(QUESMRK1) on a point and inside an area,
        // LC(QUESMRK1) along a line (an area's boundary is a plain dashed line
        // with plain boundaries, the default), in viewing group 21010.
        var symbols = instructions.OfType<PointInstruction>().Where(p => p.SymbolReference == QuestionMark).ToList();
        var lines = instructions.OfType<LineInstruction>().Where(l => l.LineStyleReference == QuestionMark).ToList();
        Assert.Equal(["500", "502"], symbols.Select(ClassOf).Order(StringComparer.Ordinal));
        Assert.Equal(["501"], lines.Select(ClassOf).Order(StringComparer.Ordinal));
        Assert.All(symbols.Concat<DrawingInstruction>(lines), i => Assert.Equal(21010, i.ViewingGroup));
    }

    [Fact]
    public void UnknownPoint_PickReportNamesTheClassAndListsAttributesAndPosition()
    {
        var processor = CreateProcessor();
        var summary = processor.EnumerateFeatures().Single(f =>
            f.FeatureType == S57UnknownObject.FeatureTypeCode && f.FeatureTypeName == "Unknown S-57 object $AREAS (500)");

        var info = processor.GetFeatureInfoAt(summary.Ordinal)!;

        Assert.Equal("Unknown S-57 object $AREAS (500)", info.FeatureTypeName);
        Assert.Equal("500", ObjectClass(info));
        // Its S-57 attributes come through by acronym, as encoded.
        Assert.Contains(info.Attributes, a => a.Code.Length == 6 && a.Code == a.Code.ToUpperInvariant());
        var position = info.Attributes.Single(a => a.Code == "position").RawValue.Split(' ');
        Assert.InRange(double.Parse(position[0], System.Globalization.CultureInfo.InvariantCulture), 41, 43);
        Assert.InRange(double.Parse(position[1], System.Globalization.CultureInfo.InvariantCulture), -72, -70);

        // And it can be picked where it is.
        var hits = processor.HitTestFeatures(
            double.Parse(position[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(position[1], System.Globalization.CultureInfo.InvariantCulture),
            radiusMeters: 10);
        Assert.Contains(hits, h => h.Ordinal == summary.Ordinal);
    }
}
