using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Rendering-correctness tests for S-111 arrow portrayal. Pins the
/// end-to-end wiring between the bundled portrayal catalogue
/// (<c>content/S111/pc/Rules/select_arrow.xsl</c>, <c>SCAROW0[1-9].svg</c>,
/// <c>ColorProfiles/colorProfile.xml</c>) and
/// <see cref="MapsuiCoverageArrowRenderer"/>:
/// per-band symbol resolution, scale-factor arithmetic, fill-colour
/// resolution, and rotation convention. Bands 1-3 share scale 0.40 by
/// spec (S-111 Ed 2.0.0, content/S111/pc/Rules/select_arrow.xsl
/// <c>scaleFloor</c>); bands 4-8 scale by <c>surfaceCurrentSpeed</c>
/// at 0.20; band 9 uses <c>scaleCeiling = 2.60</c>.
/// </summary>
public sealed class S111ArrowRenderingTests : IDisposable
{
    private const string PortrayalPath = "TestData/PortrayalCatalogue";

    private readonly IAssetSource _source;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S111PortrayalCatalogue _catalogue;

    private readonly Dictionary<string, string> _svgsByToken = new(StringComparer.OrdinalIgnoreCase);

    public S111ArrowRenderingTests()
    {
        _source = FileSystemAssetSource.Create(PortrayalPath);
        _provider = PortrayalCatalogueProvider.OpenAsync(_source).GetAwaiter().GetResult();
        _catalogue = new S111PortrayalCatalogue(_provider);
        _catalogue.SwitchPalette(PaletteType.Day);

        // Pre-load the bundled arrow SVGs so individual tests stay
        // sync (and avoid the xUnit1031 .GetResult() warning).
        foreach (var sym in _provider.Catalogue.Symbols
            .Where(s => s.Id.StartsWith("SCAROW", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = _provider.FetchAssetAsync(sym, "Symbols").GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            _svgsByToken[sym.Id] = reader.ReadToEnd();
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        _source.Dispose();
    }

    public static IEnumerable<object[]> AllBands()
    {
        // (band index 1..9, sample speed within the band)
        yield return new object[] { 1, 0.25f };
        yield return new object[] { 2, 0.75f };
        yield return new object[] { 3, 1.5f };
        yield return new object[] { 4, 2.5f };
        yield return new object[] { 5, 3.5f };
        yield return new object[] { 6, 6.0f };
        yield return new object[] { 7, 8.5f };
        yield return new object[] { 8, 11.5f };
        yield return new object[] { 9, 15.0f };
    }

    [Theory]
    [MemberData(nameof(AllBands))]
    public void SymbolRef_for_band_matches_SCAROW0N(int bandIndex, float speed)
    {
        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());
        var band = symbolScheme.Resolve(speed);

        Assert.NotNull(band);
        Assert.Equal($"SCAROW0{bandIndex}", band!.SymbolRef);
    }

    [Fact]
    public void BandScale_for_low_bands_is_constant_scaleFloor()
    {
        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());

        // Bands 1-3: defaultScaleFactor = scaleFloor (0.40), no
        // scaleAttribute. Identical scale is canonical (S-111 Ed 2.0.0
        // PC §B-9): differentiation is by colour, not size.
        foreach (var (speed, idx) in new[] { (0.1f, 0), (0.75f, 1), (1.5f, 2) })
        {
            var band = symbolScheme.Resolve(speed);
            Assert.NotNull(band);
            Assert.False(band!.ScaleByValue);
            Assert.Equal(0.40f, band.ScaleFactor);
            Assert.Equal(0.40f, BandScale(band, speed));
            _ = idx;
        }
    }

    [Fact]
    public void BandScale_for_intermediate_bands_is_speed_times_0_20()
    {
        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());

        // Bands 4-8: scaleAttribute=surfaceCurrentSpeed,
        // scaleFactor=scaleFactorIntermediate (0.20). Arrow size grows
        // linearly with speed within these bands.
        const float v = 3.5f;
        var band = symbolScheme.Resolve(v);
        Assert.NotNull(band);
        Assert.True(band!.ScaleByValue);
        Assert.Equal(0.20f, band.ScaleFactor);
        Assert.Equal(0.20f * v, BandScale(band, v), precision: 5);
    }

    [Fact]
    public void BandScale_for_top_band_is_scaleCeiling()
    {
        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());

        // Band 9: defaultScaleFactor = scaleCeiling (2.60), no
        // scaleAttribute.
        var band = symbolScheme.Resolve(15.0f);
        Assert.NotNull(band);
        Assert.False(band!.ScaleByValue);
        Assert.Equal(2.60f, band.ScaleFactor);
        Assert.Equal(2.60f, BandScale(band, 15.0f));
    }

    [Theory]
    [MemberData(nameof(AllBands))]
    public void Parsed_SVG_fill_matches_palette_SCBNn_for_each_band(int bandIndex, float speed)
    {
        _ = speed;

        // Locate the bundled SCAROW0N.svg (pre-loaded in ctor).
        var token = $"SCAROW0{bandIndex}";
        var svgContent = _svgsByToken[token];

        // Parse via the renderer's exposed helper.
        var parsed = MapsuiCoverageArrowRenderer.ParseSvgSymbol(svgContent, _catalogue.ActivePalette);

        var fill = parsed.Commands.FirstOrDefault(c => c.IsFill);
        Assert.NotNull(fill);

        // Expected colour is whatever the active palette resolves
        // SCBNn to. The SVG carries class="fSCBNn"; the renderer strips
        // the leading 'f' and looks up SCBNn.
        var expectedTokenName = $"SCBN{bandIndex}";
        Assert.True(_catalogue.ActivePalette.TryResolve(expectedTokenName, out var expectedHex));
        var rgba = RgbaColor.FromHex(expectedHex);
        var expected = new SKColor(rgba.R, rgba.G, rgba.B, rgba.A);

        Assert.Equal(expected, fill!.Color);
        // Guard against silent black fallback in ResolveToken.
        Assert.NotEqual(SKColors.Black, fill.Color);
    }

    [Fact]
    public void Day_and_Night_palettes_produce_distinguishable_band1_fill()
    {
        // Round-trip the renderer's colour resolution through both
        // palettes to confirm SwitchPalette flows end-to-end.
        var svgContent = _svgsByToken["SCAROW01"];

        _catalogue.SwitchPalette(PaletteType.Day);
        var dayParsed = MapsuiCoverageArrowRenderer.ParseSvgSymbol(svgContent, _catalogue.ActivePalette);
        var dayFill = dayParsed.Commands.First(c => c.IsFill).Color;

        _catalogue.SwitchPalette(PaletteType.Night);
        var nightParsed = MapsuiCoverageArrowRenderer.ParseSvgSymbol(svgContent, _catalogue.ActivePalette);
        var nightFill = nightParsed.Commands.First(c => c.IsFill).Color;

        Assert.NotEqual(dayFill, nightFill);
    }

    [Fact]
    public void Arrow_SVG_tip_points_up_at_rotation_zero()
    {
        // The renderer applies canvas.RotateDegrees(direction) with
        // direction in degrees true (0=N, 90=E). The bundled SVG path
        // has its tip at y=-5 within viewBox -3 -5.5 6 11, i.e. above
        // the origin. Render a single arrow at rotation 0 and assert
        // the bitmap has more non-background pixels above the pivot
        // than below — that is the empirical test of orientation.
        var svgContent = _svgsByToken["SCAROW01"];
        var parsed = MapsuiCoverageArrowRenderer.ParseSvgSymbol(svgContent, _catalogue.ActivePalette);

        const int size = 200;
        const float scale = 10f;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.Translate(size / 2f, size / 2f);
        canvas.RotateDegrees(0); // direction=N
        canvas.Scale(scale);
        foreach (var cmd in parsed.Commands)
        {
            using var paint = new SKPaint
            {
                IsAntialias = false,
                Color = cmd.Color,
                Style = cmd.IsFill ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                StrokeWidth = cmd.StrokeWidth,
            };
            canvas.DrawPath(cmd.Path, paint);
        }
        canvas.Restore();

        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        int above = 0, below = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var c = pixmap.GetPixelColor(x, y);
                if (c != SKColors.White)
                {
                    if (y < size / 2) above++;
                    else if (y > size / 2) below++;
                }
            }
        }

        // SVG tip at y=-5 is in canvas-up direction; Skia y-axis grows
        // downward; canvas.Translate puts origin at centre. After
        // scaling, the painted arrow should occupy primarily above the
        // pivot (y<size/2).
        Assert.True(above > below,
            $"Arrow tip should point up at rotation 0 (above={above}, below={below}).");
    }

    private static float BandScale(SymbolBand band, float value)
        => band.ScaleByValue ? band.ScaleFactor * value : band.ScaleFactor;
}
