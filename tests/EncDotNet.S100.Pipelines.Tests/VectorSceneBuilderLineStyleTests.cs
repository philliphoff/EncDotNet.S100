using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// How <see cref="VectorSceneBuilder"/> lowers S-100 Part 9 line styles (#1141):
/// a named style wins over the instruction's carried-over colour, its dashes
/// repeat over its interval, its symbols ride along the line, and a composite
/// style stacks its parts.
/// </summary>
public sealed class VectorSceneBuilderLineStyleTests
{
    private sealed class Line : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => new()
        {
            Type = GeometryType.Curve,
            Coordinates = [new GeoPosition(50, -1), new GeoPosition(50, -0.9)],
        };
    }

    private static readonly Dictionary<string, RgbaColor> Colours = new()
    {
        ["CHMGD"] = new RgbaColor(200, 0, 200, 255),
        ["CSTLN"] = new RgbaColor(80, 80, 80, 255),
        ["CHBLK"] = new RgbaColor(0, 0, 0, 255),
        ["CHYLW"] = new RgbaColor(255, 255, 0, 255),
    };

    /// <summary>CTNARE51 from the S-101 portrayal catalogue: three dashes and four symbols in 32.3 mm.</summary>
    private static readonly LineStyle Caution = new()
    {
        Name = "CTNARE51",
        Width = 0.32f,
        Color = "CHMGD",
        DashPattern = [2f, 6f, 18.2f, 6f, 26.2f, 6f],
        IntervalLength = 32.3f,
        Symbols =
        [
            new LineStyleSymbol("EMAREMG1", 5f),
            new LineStyleSymbol("EMCTNAR1", 13.1f),
        ],
    };

    private static IReadOnlyList<PaintOp> Build(LineInstruction instruction, params LineStyle[] styles)
    {
        var builder = new VectorSceneBuilder
        {
            ResolveColor = token => Colours.TryGetValue(token ?? "", out var c) ? c : default,
            LineStyleProvider = name => styles.FirstOrDefault(s => s.Name == name),
            SymbolResolver = name => new SymbolAsset($"<svg id='{name}'/>", 0, 0),
        };
        return builder.Build([instruction], new Line()).Ops;
    }

    [Fact]
    public void NamedStyle_WinsOverTheCarriedOverColour()
    {
        // The Lua parser leaves its default CSTLN colour on every line.
        var op = Assert.IsType<LinePaintOp>(Assert.Single(Build(
            new LineInstruction { FeatureReference = "f", LineStyleReference = "CTNARE51", LineColor = "CSTLN" },
            Caution)));

        Assert.Equal(Colours["CHMGD"], op.Color);
        Assert.Equal(1.0, op.WidthPx);
    }

    [Fact]
    public void NamedStyle_DashesRepeatOverItsInterval()
    {
        var op = Assert.IsType<LinePaintOp>(Assert.Single(Build(
            new LineInstruction { FeatureReference = "f", LineStyleReference = "CTNARE51" }, Caution)));

        // on 6, off 10.2 (to 18.2), on 6, off 2, on 6, off 2.1 (to 2 + 32.3); /0.32 mm per px.
        Assert.Equal(
            new[] { 6.0, 10.2, 6.0, 2.0, 6.0, 2.1 }.Select(mm => mm / 0.32),
            op.DashArrayPx!.Select(px => (double)px),
            new ToleranceComparer(0.01));
        Assert.Equal((32.3 - 2) / 0.32, op.DashPhasePx, 3);
    }

    [Fact]
    public void NamedStyle_PlacesItsSymbolsAlongTheLine()
    {
        var op = Assert.IsType<LinePaintOp>(Assert.Single(Build(
            new LineInstruction { FeatureReference = "f", LineStyleReference = "CTNARE51" }, Caution)));

        Assert.Equal(32.3 / 0.32, op.SymbolIntervalPx, 3);
        Assert.Equal(["<svg id='EMAREMG1'/>", "<svg id='EMCTNAR1'/>"], op.Symbols.Select(s => s.Symbol.ProcessedSvg));
        Assert.Equal(13.1 / 0.32, op.Symbols[1].PositionPx, 3);
    }

    [Fact]
    public void CompositeStyle_StacksItsParts()
    {
        var composite = new LineStyle
        {
            Name = "INDHLT02",
            Width = 1.28f,
            Color = "CHBLK",
            Components =
            [
                new LineStyle { Name = "INDHLT02", Width = 1.28f, Color = "CHBLK" },
                new LineStyle { Name = "INDHLT02", Width = 0.64f, Color = "CHYLW" },
            ],
        };

        var ops = Build(new LineInstruction { FeatureReference = "f", LineStyleReference = "INDHLT02" }, composite)
            .Cast<LinePaintOp>().ToList();

        Assert.Equal([Colours["CHBLK"], Colours["CHYLW"]], ops.Select(o => o.Color));
        Assert.Equal([4.0, 2.0], ops.Select(o => o.WidthPx), new ToleranceComparer(0.001));
    }

    [Fact]
    public void SimpleDash_IsAStartAndLengthWithinTheInterval()
    {
        // S-101 SimpleLineStyle('dot', …): Dash:0,0.6;LineStyle:_simple_,1.8,…
        var op = Assert.IsType<LinePaintOp>(Assert.Single(Build(new LineInstruction
        {
            FeatureReference = "f",
            LineStyleReference = LineInstruction.SimpleLineStyleReference,
            LineColor = "CHBLK",
            LineWidth = 0.64,
            Dashes = [(0, 0.6)],
            DashIntervalLength = 1.8,
        })));

        Assert.Equal(new[] { 0.6 / 0.32, 1.2 / 0.32 }, op.DashArrayPx!.Select(px => (double)px), new ToleranceComparer(0.01));
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
