using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

public class VectorPipelineTests
{
    [Fact]
    public async Task ProcessAsync_PointFeature_ProducesSymbolInstruction()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "Buoy", GeometryType.Point, (47.6, -122.3)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = layer.Instructions[0];
        Assert.Equal(DrawingType.Symbol, inst.Type);
        Assert.Equal("Buoy", inst.Feature.FeatureType);
        Assert.Equal(DisplayPlane.OverRadar, inst.Plane);
    }

    [Fact]
    public async Task ProcessAsync_CurveFeature_ProducesLineInstruction()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "CoastLine", GeometryType.Curve, (47.6, -122.3), (47.7, -122.2)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        Assert.Equal(DrawingType.Line, layer.Instructions[0].Type);
    }

    [Fact]
    public async Task ProcessAsync_SurfaceFeature_ProducesAreaInstruction()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "DepthArea", GeometryType.Surface,
                (47.5, -122.4), (47.6, -122.4), (47.6, -122.3), (47.5, -122.3)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        Assert.Equal(DrawingType.Area, layer.Instructions[0].Type);
        Assert.Equal(DisplayPlane.UnderRadar, layer.Instructions[0].Plane);
    }

    [Fact]
    public async Task ProcessAsync_NoneGeometry_Skipped()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "MetaInfo", GeometryType.None, (0, 0)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Instructions);
    }

    [Fact]
    public async Task ProcessAsync_HiddenViewingGroup_Filtered()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "Buoy", GeometryType.Point, (47.6, -122.3)),
        ]);

        var viewingGroups = new ViewingGroupController();
        viewingGroups.SetVisible(21010, false);

        var catalogue = new FakeVectorPortrayalCatalogue(viewingGroups);
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Instructions);
    }

    [Fact]
    public async Task ProcessAsync_MixedFeatures_SortedByDisplayPlane()
    {
        var source = new FakeVectorSource(
        [
            // Point → OverRadar, Surface → UnderRadar
            MakeFeature(1, "Buoy", GeometryType.Point, (47.6, -122.3)),
            MakeFeature(2, "DepthArea", GeometryType.Surface,
                (47.5, -122.4), (47.6, -122.4), (47.6, -122.3)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(2, layer.Instructions.Count);
        // UnderRadar (surface) should sort before OverRadar (point)
        Assert.Equal(DisplayPlane.UnderRadar, layer.Instructions[0].Plane);
        Assert.Equal(DisplayPlane.OverRadar, layer.Instructions[1].Plane);
    }

    [Fact]
    public async Task ProcessAsync_ResolvedRuleName_PassedThrough()
    {
        var source = new FakeVectorSource(
        [
            MakeFeature(1, "Buoy", GeometryType.Point, (47.6, -122.3)),
        ]);

        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // FakeVectorPortrayalCatalogue returns "Rule_{featureType}"
        Assert.Equal("Rule_Buoy", layer.Instructions[0].RuleName);
    }

    [Fact]
    public async Task ProcessAsync_EmptySource_ProducesEmptyLayer()
    {
        var source = new FakeVectorSource([]);
        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Instructions);
    }

    [Fact]
    public async Task ProcessAsync_Metadata_PassesThrough()
    {
        var source = new FakeVectorSource([]);
        var catalogue = new FakeVectorPortrayalCatalogue();
        var pipeline = new VectorPipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal("S-101", layer.Metadata.ProductSpec);
        Assert.Equal("EPSG:4326", layer.Metadata.HorizontalCRS);
    }

    #region Helpers

    private static Feature MakeFeature(
        long id,
        string featureType,
        GeometryType geometryType,
        params (double Lat, double Lon)[] coordinates) =>
        new()
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = geometryType,
            Coordinates = coordinates.Select(c => (c.Lat, c.Lon)).ToList(),
            Attributes = new Dictionary<string, object?>(),
        };

    #endregion

    #region Fakes

    private sealed class FakeVectorSource : IVectorSource
    {
        private readonly IReadOnlyList<Feature> _features;

        public FakeVectorSource(IReadOnlyList<Feature> features)
        {
            _features = features;
        }

        public VectorMetadata Metadata => new()
        {
            ProductSpec = "S-101",
            Extent = new BoundingBox(47.0, -123.0, 48.0, -122.0),
            HorizontalCRS = "EPSG:4326",
            CompilationScaleDenominator = 22_000,
        };

        public IReadOnlyList<Feature> GetFeatures(BoundingBox? extent = null) => _features;
    }

    private sealed class FakeVectorPortrayalCatalogue : IVectorPortrayalCatalogue
    {
        public FakeVectorPortrayalCatalogue(ViewingGroupController? viewingGroups = null)
        {
            ViewingGroups = viewingGroups ?? new ViewingGroupController();
        }

        public string ProductSpec => "S-101";
        public string Edition => "1.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public void SwitchPalette(PaletteType type) { }

        public ViewingGroupController ViewingGroups { get; }

        public string ResolveRule(string featureType, IReadOnlyDictionary<string, object?> attributes) =>
            $"Rule_{featureType}";

        public XslCompiledTransform GetRule(string ruleName) => throw new NotImplementedException();
        public Script GetLuaScript(string scriptName) => throw new NotImplementedException();
        public SvgSymbol GetSymbol(string symbolName) => throw new NotImplementedException();
        public LineStyle GetLineStyle(string name) => throw new NotImplementedException();
        public AreaFill GetAreaFill(string name) => throw new NotImplementedException();
    }

    #endregion
}
