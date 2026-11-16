using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Covers <see cref="SymbolGeometryParser"/> and <see cref="SvgSymbol.Geometry"/>:
/// catalogue symbols handed out as paths, circles and palette tokens (#1513).
/// </summary>
public sealed class SymbolGeometryParserTests
{
    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";

    [Fact]
    public void ClassConventions_BecomeTokenPaint()
    {
        var g = Parse($"""
            <svg {Ns} viewBox="-1 -2 3 4">
              <path d="M 0,0 L 1,0 L 1,1 Z" class="fCHGRN"/>
              <path d="M 0,0 L 1,1" class="sl f0 sOUTLW" stroke-width="0.32"/>
            </svg>
            """);

        Assert.Equal(new SymbolRect(-1, -2, 3, 4), g.ViewBox);
        Assert.Equal(2, g.Shapes.Count);

        var fill = g.Shapes[0].Paint;
        Assert.Equal("CHGRN", fill.Fill?.Token);
        Assert.Null(fill.Stroke);

        var stroke = g.Shapes[1].Paint;
        Assert.Null(stroke.Fill);
        Assert.Equal("OUTLW", stroke.Stroke?.Token);
        Assert.Equal(0.32, stroke.StrokeWidth, 6);
        Assert.Equal(SymbolLineCap.Round, stroke.LineCap);
        Assert.Equal(SymbolLineJoin.Round, stroke.LineJoin);
    }

    [Fact]
    public void LayoutElements_AreNotDrawn_PivotIsRead()
    {
        var g = Parse($"""
            <svg {Ns} viewBox="-2 -2 4 4">
              <rect class="symbolBox layout" fill="none" x="-2" y="-2" width="4" height="4"/>
              <circle class="fCHBLK" cx="0" cy="0" r="0.5"/>
              <circle class="pivotPoint layout" fill="none" cx="0.25" cy="-0.5" r="1"/>
            </svg>
            """);

        var circle = Assert.IsType<SymbolCircle>(Assert.Single(g.Shapes));
        Assert.Equal(0.5, circle.Radius);
        Assert.True(g.HasPivot);
        Assert.Equal((0.25, -0.5), g.Pivot);
    }

    [Fact]
    public void AttributeBeatsClass_StyleBeatsAttribute_RootInherits()
    {
        var g = Parse($"""
            <svg {Ns} style="fill-rule:evenodd" viewBox="0 0 1 1">
              <rect class="fCHRED" fill="none" x="0" y="0" width="1" height="1"/>
              <circle class="sl f0 sCHBLK" stroke-width="0.1" style="stroke-width: 0.32;" cx="0" cy="0" r="1"/>
              <path class="fDNGHL" fill-opacity="0.25" d="M0 0 1 0 1 1z"/>
            </svg>
            """);

        Assert.Null(g.Shapes[0].Paint.Fill);
        Assert.Equal(0.32, g.Shapes[1].Paint.StrokeWidth, 6);
        Assert.Equal(0.25, g.Shapes[2].Paint.FillOpacity, 6);
        Assert.All(g.Shapes, s => Assert.Equal(SymbolFillRule.EvenOdd, s.Paint.FillRule));
    }

    [Fact]
    public void UnstyledShape_GetsSvgDefaultBlackFill()
    {
        var g = Parse($"""<svg {Ns}><path d="M0,0 L1,1"/></svg>""");

        var paint = Assert.Single(g.Shapes).Paint;
        Assert.Equal(SymbolColor.DefaultFill, paint.Fill);
        Assert.Null(paint.Stroke);
    }

    [Fact]
    public void PathData_IsAbsolute_AndCoversEveryCommand()
    {
        var g = Parse($"""
            <svg {Ns}>
              <path d="m1 1 2 0 h1 v1 l-1,1 z M0,0 Q 1 1 2 0 T 4 0 C 4 1 5 1 5 0 S 6 -1 6 0 A 1 1 0 01 7 0"/>
            </svg>
            """);

        var segments = Assert.IsType<SymbolPath>(Assert.Single(g.Shapes)).Segments;
        Assert.Equal(new SymbolMoveTo(1, 1), segments[0]);
        Assert.Equal(new SymbolLineTo(3, 1), segments[1]);   // implicit l after m
        Assert.Equal(new SymbolLineTo(4, 1), segments[2]);   // h
        Assert.Equal(new SymbolLineTo(4, 2), segments[3]);   // v
        Assert.Equal(new SymbolLineTo(3, 3), segments[4]);   // l
        Assert.Same(SymbolClosePath.Instance, segments[5]);
        Assert.Equal(new SymbolMoveTo(0, 0), segments[6]);

        var q = Assert.IsType<SymbolCubicTo>(segments[7]);
        Assert.Equal(2.0 / 3, q.X1, 9);
        Assert.Equal(2.0 / 3, q.Y1, 9);
        Assert.Equal(4.0 / 3, q.X2, 9);
        Assert.Equal(2.0 / 3, q.Y2, 9);
        Assert.Equal((2.0, 0.0), (q.X, q.Y));
        var t = Assert.IsType<SymbolCubicTo>(segments[8]);    // reflected control (3, -1)
        Assert.Equal((4.0, 0.0), (t.X, t.Y));
        Assert.Equal(2 + 2.0 / 3 * (3 - 2), t.X1, 9);
        Assert.Equal(2.0 / 3 * -1, t.Y1, 9);

        Assert.Equal(new SymbolCubicTo(4, 1, 5, 1, 5, 0), segments[9]);
        Assert.Equal(new SymbolCubicTo(5, -1, 6, -1, 6, 0), segments[10]);   // S reflects (5,1) about (5,0)
        Assert.Equal(new SymbolArcTo(1, 1, 0, false, true, 7, 0), segments[11]); // packed flags "01"
        Assert.Equal(12, segments.Count);
    }

    [Fact]
    public void RectAndPolygons_BecomePaths()
    {
        var g = Parse($"""
            <svg {Ns}>
              <rect x="1" y="2" width="3" height="4" class="fCHWHT"/>
              <polygon points="0,3 1.5,0 3,3" class="fCHRED"/>
              <polyline points="0 0 1 1" class="f0 sCHBLK"/>
            </svg>
            """);

        var rect = Assert.IsType<SymbolPath>(g.Shapes[0]).Segments;
        Assert.Equal(
            [new SymbolMoveTo(1, 2), new SymbolLineTo(4, 2), new SymbolLineTo(4, 6), new SymbolLineTo(1, 6), SymbolClosePath.Instance],
            rect);

        var polygon = Assert.IsType<SymbolPath>(g.Shapes[1]).Segments;
        Assert.Equal(4, polygon.Count);
        Assert.IsType<SymbolClosePath>(polygon[^1]);

        var polyline = Assert.IsType<SymbolPath>(g.Shapes[2]).Segments;
        Assert.Equal([new SymbolMoveTo(0, 0), new SymbolLineTo(1, 1)], polyline);
    }

    [Fact]
    public void TextTransformAndPatternPaint_AreReportedNotGuessed()
    {
        var g = Parse($"""
            <svg {Ns}>
              <text x="0" y="3.5" fill="black">?</text>
              <path transform="rotate(45)" d="M0,0 L1,1"/>
              <polygon points="0,0 1,0 1,1" fill="url(#Triangle)"/>
              <circle class="fCHBLK" cx="0" cy="0" r="1"/>
            </svg>
            """);

        Assert.IsType<SymbolCircle>(Assert.Single(g.Shapes));
        Assert.Equal(["text", "path", "polygon"], g.Unsupported);
    }

    [Fact]
    public void MalformedSvg_ReturnsNull()
    {
        Assert.Null(SymbolGeometryParser.TryParse("<svg", "X"));
        Assert.Null(SymbolGeometryParser.TryParse("", "X"));
    }

    [Fact]
    public void SymbolColor_ResolvesTokensAgainstPalette()
    {
        var palette = new ColorPalette("Day", new Dictionary<string, string> { ["CHGRN"] = "#00FF00" });

        Assert.Equal("#00FF00", SymbolColor.FromToken("CHGRN").Resolve(palette));
        Assert.Equal("#000000", SymbolColor.FromToken("NOPE").Resolve(palette));
        Assert.Equal("red", SymbolColor.FromLiteral("red").Resolve(palette));
    }

    [Fact]
    public async Task Boylat13_FromCatalogue_IsTriangleOutlineAndDot()
    {
        var catalogue = await OpenS101Async();

        var symbol = await catalogue.GetSymbolAsync("BOYLAT13", TestContext.Current.CancellationToken);
        var g = symbol.Geometry;

        Assert.NotNull(g);
        Assert.Same(g, symbol.Geometry); // parsed once, kept with the record
        Assert.Equal("BOYLAT13", g.Name);
        Assert.Equal(new SymbolRect(-3.25, -3.13, 4.65, 4.71), g.ViewBox);
        Assert.True(g.HasPivot);
        Assert.Equal((0.0, 0.0), g.Pivot);
        Assert.Empty(g.Unsupported);
        Assert.Equal(3, g.Shapes.Count);

        var body = Assert.IsType<SymbolPath>(g.Shapes[0]);
        Assert.Equal("CHGRN", body.Paint.Fill?.Token);
        Assert.Equal(SymbolFillRule.EvenOdd, body.Paint.FillRule);
        Assert.Equal(new SymbolMoveTo(1.24, 1.42), body.Segments[0]);
        Assert.IsType<SymbolClosePath>(body.Segments[^1]);

        var outline = Assert.IsType<SymbolPath>(g.Shapes[1]);
        Assert.Null(outline.Paint.Fill);
        Assert.Equal("OUTLW", outline.Paint.Stroke?.Token);
        Assert.Equal(0.32, outline.Paint.StrokeWidth, 6);

        var dot = Assert.IsType<SymbolCircle>(g.Shapes[2]);
        Assert.Equal("OUTLW", dot.Paint.Fill?.Token);
        Assert.Equal(0.15, dot.Radius, 6);
    }

    [Theory]
    [InlineData("LIGHTS11")] // sector light flare, fill-opacity
    [InlineData("DNGHILIT")] // highlight square with fill-opacity 0.25
    [InlineData("SOUNDG15")] // sounding digit drawn from paths
    public async Task RealSymbols_ParseWithoutLoss(string name)
    {
        var catalogue = await OpenS101Async();

        var g = (await catalogue.GetSymbolAsync(name, TestContext.Current.CancellationToken)).Geometry;

        Assert.NotNull(g);
        Assert.NotEmpty(g.Shapes);
        Assert.Empty(g.Unsupported);
        Assert.All(g.Shapes, s => Assert.True(s.Paint.Fill?.Token is not null || s.Paint.Stroke?.Token is not null));
    }

    /// <summary>
    /// Every S-101 catalogue symbol converts with nothing dropped, and agrees
    /// with what the SVG renderers draw: parsing the renderer's processed SVG
    /// (colours inlined by <see cref="SvgProcessor"/>) yields the same shapes
    /// in the same colours as resolving the geometry's tokens.
    /// </summary>
    [Fact]
    public async Task AllS101Symbols_MatchTheRendererProcessedSvg()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await OpenS101ProviderAsync();
        var catalogue = new S101PortrayalCatalogue(provider);
        await catalogue.SwitchPaletteAsync(PaletteType.Day, ct);
        var palette = catalogue.ActivePalette;

        var names = provider.Catalogue.Symbols.Select(s => s.Id).ToList();
        Assert.True(names.Count > 500);

        foreach (var name in names)
        {
            var symbol = await catalogue.GetSymbolAsync(name, ct);
            var g = symbol.Geometry;
            Assert.NotNull(g);
            Assert.True(g.Unsupported.Count == 0, $"{name}: unsupported {string.Join(",", g.Unsupported)}");

            var rendered = SymbolGeometryParser.TryParse(SvgProcessor.Process(symbol.SvgContent, palette), name);
            Assert.NotNull(rendered);
            Assert.True(g.Shapes.Count == rendered.Shapes.Count, $"{name}: shape count");

            for (var i = 0; i < g.Shapes.Count; i++)
            {
                var a = g.Shapes[i];
                var b = rendered.Shapes[i];
                Assert.Equal(a.GetType(), b.GetType());
                Assert.True(
                    Resolved(a.Paint.Fill, palette) == Resolved(b.Paint.Fill, palette),
                    $"{name} shape {i}: fill {a.Paint.Fill} vs {b.Paint.Fill}");
                Assert.True(
                    Resolved(a.Paint.Stroke, palette) == Resolved(b.Paint.Stroke, palette),
                    $"{name} shape {i}: stroke {a.Paint.Stroke} vs {b.Paint.Stroke}");
                Assert.Equal(a.Paint with { Fill = null, Stroke = null }, b.Paint with { Fill = null, Stroke = null });
                if (a is SymbolPath pa)
                    Assert.Equal(pa.Segments, ((SymbolPath)b).Segments);
            }
        }
    }

    private static string? Resolved(SymbolColor? color, ColorPalette palette) =>
        color?.Resolve(palette).ToUpperInvariant();

    private static SymbolGeometry Parse(string svg) =>
        SymbolGeometryParser.TryParse(svg, "TEST") ?? throw new InvalidOperationException("parse failed");

    private static async Task<S101PortrayalCatalogue> OpenS101Async() =>
        new(await OpenS101ProviderAsync());

    private static async Task<PortrayalCatalogueProvider> OpenS101ProviderAsync()
    {
        var source = Specification.CreatePortrayalCatalogueSource("S-101");
        return await PortrayalCatalogueProvider.OpenAsync(source, cancellationToken: TestContext.Current.CancellationToken);
    }
}
