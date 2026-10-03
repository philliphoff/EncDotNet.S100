using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Rendering-correctness tests for S-111 arrow portrayal.  Pins the
/// end-to-end wiring between the bundled portrayal catalogue
/// (<c>content/S111/pc/Rules/select_arrow.xsl</c>, <c>SCAROW0[1-9].svg</c>,
/// <c>ColorProfiles/colorProfile.xml</c>) and
/// <see cref="MapsuiCoverageArrowRenderer"/>: per-band symbol resolution,
/// scale-factor arithmetic, palette-driven fill-colour inlining, and
/// rotation convention.  Bands 1-3 share scale 0.40 by spec (S-111
/// Ed 2.0.0 PC §B-9 <c>scaleFloor</c>); bands 4-8 scale by
/// <c>surfaceCurrentSpeed</c> at 0.20; band 9 uses
/// <c>scaleCeiling = 2.60</c>.
/// </summary>
public sealed class S111ArrowRenderingTests : IDisposable
{
    private const string PortrayalPath = "TestData/PortrayalCatalogue";

    private readonly IAssetSource _source;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S111PortrayalCatalogue _catalogue;

    private readonly Dictionary<string, string> _svgsByToken =
        new(StringComparer.OrdinalIgnoreCase);

    public S111ArrowRenderingTests()
    {
        _source = FileSystemAssetSource.Create(PortrayalPath);
        _provider = PortrayalCatalogueProvider.OpenAsync(_source).GetAwaiter().GetResult();
        _catalogue = new S111PortrayalCatalogue(_provider);
        _catalogue.SwitchPaletteAsync(PaletteType.Day).AsTask().GetAwaiter().GetResult();

        // Pre-load the bundled arrow SVGs synchronously in the ctor so
        // individual tests avoid xUnit1031 .GetResult() warnings.
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
        // scaleAttribute.  Identical scale is canonical (S-111 Ed 2.0.0
        // PC §B-9): differentiation is by colour, not size.
        foreach (var speed in new[] { 0.1f, 0.75f, 1.5f })
        {
            var band = symbolScheme.Resolve(speed);
            Assert.NotNull(band);
            Assert.False(band!.ScaleByValue);
            Assert.Equal(0.40f, band.ScaleFactor);
            Assert.Equal(0.40f, BandScale(band, speed));
        }
    }

    [Fact]
    public void BandScale_for_intermediate_bands_is_speed_times_0_20()
    {
        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());

        // Bands 4-8: scaleAttribute=surfaceCurrentSpeed,
        // scaleFactor=scaleFactorIntermediate (0.20).
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
    public void Resolved_SVG_inlines_palette_fill_hex_for_each_band(int bandIndex, float speed)
    {
        _ = speed;

        var renderer = CreateRenderer();
        var token = $"SCAROW0{bandIndex}";
        var resolved = renderer.GetResolvedSvg(token);

        Assert.NotNull(resolved);
        Assert.StartsWith("svg-content://", resolved);

        // Expected colour is whatever the active palette resolves SCBNn
        // to.  The SVG carries class="fSCBNn"; SvgProcessor strips the
        // class and inlines a fill attribute with the palette's hex
        // value.
        var expectedTokenName = $"SCBN{bandIndex}";
        Assert.True(_catalogue.ActivePalette.TryResolve(expectedTokenName, out var expectedHex));

        Assert.Contains(expectedHex, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Day_and_Night_palettes_produce_different_resolved_SVG()
    {
        var renderer = CreateRenderer();

        await _catalogue.SwitchPaletteAsync(PaletteType.Day);
        renderer.Palette = _catalogue.ActivePalette;
        var daySvg = renderer.GetResolvedSvg("SCAROW01");

        await _catalogue.SwitchPaletteAsync(PaletteType.Night);
        renderer.Palette = _catalogue.ActivePalette;
        var nightSvg = renderer.GetResolvedSvg("SCAROW01");

        Assert.NotNull(daySvg);
        Assert.NotNull(nightSvg);
        Assert.NotEqual(daySvg, nightSvg);
    }

    [Fact]
    public void Render_emits_one_feature_per_grid_cell_with_per_band_scale_and_rotation()
    {
        // 1×3 coverage with speeds chosen to hit bands 1, 5, 9 and
        // directions covering 0°, 90°, 180°.
        const string valueField = "surfaceCurrentSpeed";
        const string rotationField = "surfaceCurrentDirection";

        var metadata = new GridMetadata
        {
            NumRows = 1,
            NumColumns = 3,
            OriginLongitude = 0.0,
            OriginLatitude = 0.0,
            SpacingLongitudinal = 1.0,
            SpacingLatitudinal = 1.0,
        };

        var coverage = new SampledCoverage
        {
            Region = GridRegion.Full,
            Metadata = metadata,
            Values = new Dictionary<string, float[]>
            {
                [valueField] = new float[] { 0.25f, 3.5f, 15.0f },
                [rotationField] = new float[] { 0f, 90f, 180f },
            },
        };

        var symbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());
        var layer = new StyledCoverageLayer
        {
            Coverage = coverage,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            SymbolScheme = symbolScheme,
            NoDataValue = float.NaN,
        };

        var renderer = CreateRenderer();
        var result = renderer.Render(layer);

        // Zoomed in far enough (1 m/px) that the 1° cells never need thinning.
        var thinned = Assert.IsType<ThinnedSymbolLayer>(result);
        var features = thinned.GetFeatures(thinned.Extent!.Grow(1000), resolution: 1.0)
            .OfType<PointFeature>()
            .OrderBy(f => f.Point.X)
            .ToList();
        Assert.Equal(3, features.Count);

        // Band 1, speed 0.25, scale 0.40 → SymbolScale = 2.0 × 0.40
        AssertImageStyle(features[0], expectedScale: 2.0 * 0.40, expectedRotation: 0.0);
        // Band 5, speed 3.5, scale 0.20 × 3.5 = 0.70 → SymbolScale = 2.0 × 0.70
        AssertImageStyle(features[1], expectedScale: 2.0 * 0.20 * 3.5, expectedRotation: 90.0);
        // Band 9, speed 15, scale 2.60 → SymbolScale = 2.0 × 2.60
        AssertImageStyle(features[2], expectedScale: 2.0 * 2.60, expectedRotation: 180.0);
    }

    [Fact]
    public void Render_returns_null_when_layer_has_no_symbol_scheme()
    {
        var metadata = new GridMetadata
        {
            NumRows = 1,
            NumColumns = 1,
            OriginLongitude = 0.0,
            OriginLatitude = 0.0,
            SpacingLongitudinal = 1.0,
            SpacingLatitudinal = 1.0,
        };

        var layer = new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]>(),
            },
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            SymbolScheme = null,
            NoDataValue = float.NaN,
        };

        var renderer = CreateRenderer();
        var result = renderer.Render(layer);

        Assert.Null(result);
    }

    [Fact]
    public void Symbol_scheme_carries_the_spec_arrow_length_and_thinning_ratio()
    {
        var scheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());

        // S-111 Ed 2.0.0 Figure 9-1: the arrow spans y = -5 … +5 mm.
        Assert.Equal(10.0, scheme.NominalSymbolLengthMillimetres);
        // S-111 §9.3.2 / S-98 Appendix G-1.1: Rmax = 0.5.
        Assert.Equal(0.5, scheme.MaxSymbolToSpacingRatio);
    }

    [Theory]
    [InlineData(0.25f, 4.0)]   // below Slow (2 kn): 10 mm × 2 / 5
    [InlineData(2.0f, 4.0)]    // at Slow
    [InlineData(5.0f, 10.0)]   // at Sref: Href
    [InlineData(10.0f, 20.0)]
    [InlineData(13.0f, 26.0)]  // at Shigh
    [InlineData(20.0f, 26.0)]  // above Shigh: capped
    public void Arrow_length_follows_Eqn_9_1(float speed, double expectedMillimetres)
    {
        // H = Href · min(max(Slow, S), Shigh) / Sref with Href = 10 mm,
        // Sref = 5 kn, Slow = 2 kn, Shigh = 13 kn (S-111 Ed 2.0.0 Eqn 9.1).
        var scheme = _catalogue.ResolveSymbolScheme(new MarinerSettings());
        var band = scheme.Resolve(speed);
        Assert.NotNull(band);

        double length = scheme.NominalSymbolLengthMillimetres * band!.ScaleFor(speed);
        Assert.Equal(expectedMillimetres, length, precision: 4);
    }

    [Fact]
    public void Zooming_out_thins_the_grid_by_the_S98_increment()
    {
        // 40×40 cells 0.01° apart at the equator, all 0.25 kn (band 1, scale 0.40).
        var layer = BuildUniformLayer(rows: 40, cols: 40, spacingDegrees: 0.01, speed: 0.25f);
        var thinned = Assert.IsType<ThinnedSymbolLayer>(CreateRenderer().Render(layer));
        var everything = thinned.Extent!.Grow(10_000);

        // At 1 m/px every cell is ~1113 px apart: nothing is thinned.
        Assert.Equal(1600, thinned.GetFeatures(everything, resolution: 1.0).Count());

        // At 100 m/px: D ≈ 1574 m / 100 = 15.7 px; Lsmax = 10 mm × 0.40 × 2.0
        // (BaseSymbolScale) = 8 mm ≈ 30.2 px. Lsmax/D ≥ 0.5, so
        // n = 1 + fix(30.2 / (15.7 × 0.5)) = 4: every 4th row and column.
        var features = thinned.GetFeatures(everything, resolution: 100.0).OfType<PointFeature>().ToList();
        Assert.Equal(10 * 10, features.Count);

        // Eqn 9.2 bounds the drawn cell diagonal: nD > Lsmax / Rmax = 2 × arrow
        // length. Along an axis that is nD / √2 — still clear of the arrow
        // length, so neighbouring arrows cannot overlap.
        double minSpacing = MinPairDistance(features) / 100.0;
        Assert.True(minSpacing * Math.Sqrt(2) >= 2 * 30.2, $"spacing {minSpacing:F1} px");
        Assert.True(minSpacing > 30.2, $"spacing {minSpacing:F1} px");
    }

    [Fact]
    public void Layer_extent_is_the_grid_extent_even_when_only_one_column_has_data()
    {
        // A mostly-land tile: only column 3 has currents. The layer extent must
        // still be the grid's, or fit-to-extent collapses to a zero-width line.
        var layer = BuildUniformLayer(rows: 5, cols: 5, spacingDegrees: 0.01, speed: float.NaN);
        var speeds = layer.Coverage.Values["surfaceCurrentSpeed"];
        for (int r = 0; r < 5; r++)
            speeds[(r * 5) + 3] = 0.5f;

        var thinned = Assert.IsType<ThinnedSymbolLayer>(CreateRenderer().Render(layer));

        Assert.True(thinned.Extent!.Width > 0.03 * 111_000, $"extent width {thinned.Extent.Width}");
        Assert.Equal(5, thinned.GetFeatures(thinned.Extent.Grow(1000), resolution: 1.0).Count());
    }

    [Fact]
    public void Panning_keeps_the_lattice_anchored_on_the_fastest_current()
    {
        // A slow field (every arrow at the Slow size) with one faster cell
        // that stays in view: the lattice is seeded on it, so a small pan
        // does not shift which cells are drawn.
        var layer = BuildUniformLayer(rows: 40, cols: 40, spacingDegrees: 0.01, speed: 0.25f, fastCell: (21, 18));
        var thinned = Assert.IsType<ThinnedSymbolLayer>(CreateRenderer().Render(layer));
        var view = new Mapsui.MRect(10_000, 10_000, 40_000, 40_000);

        var before = thinned.GetFeatures(view, resolution: 100.0).OfType<PointFeature>()
            .Select(f => (f.Point.X, f.Point.Y)).ToHashSet();
        var after = thinned.GetFeatures(new Mapsui.MRect(10_500, 10_300, 40_500, 40_300), resolution: 100.0)
            .OfType<PointFeature>().Select(f => (f.Point.X, f.Point.Y)).ToHashSet();

        Assert.NotEmpty(before);
        var common = before.Intersect(after).Count();
        Assert.True(common >= before.Count - 12, $"only {common} of {before.Count} arrows stayed put");
    }

    private MapsuiCoverageArrowRenderer CreateRenderer() =>
        new(new IdentityCrsTransformFactory())
        {
            Palette = _catalogue.ActivePalette,
            SymbolProvider = name => _svgsByToken.TryGetValue(name, out var svg) ? svg : null,
            // Pin to 2.0 so arithmetic in tests is independent of the
            // default value, which is driven by the user-facing
            // RenderContext.SymbolScale in production.
            BaseSymbolScale = 2.0,
        };

    private StyledCoverageLayer BuildUniformLayer(
        int rows, int cols, double spacingDegrees, float speed, (int Row, int Col)? fastCell = null)
    {
        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = cols,
            OriginLongitude = 0.0,
            OriginLatitude = 0.0,
            SpacingLongitudinal = spacingDegrees,
            SpacingLatitudinal = spacingDegrees,
        };

        var speeds = Enumerable.Repeat(speed, rows * cols).ToArray();
        if (fastCell is { } fast)
            speeds[fast.Row * cols + fast.Col] = speed + 0.1f;

        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]>
                {
                    ["surfaceCurrentSpeed"] = speeds,
                    ["surfaceCurrentDirection"] = new float[rows * cols],
                },
            },
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            SymbolScheme = _catalogue.ResolveSymbolScheme(new MarinerSettings()),
            NoDataValue = float.NaN,
        };
    }

    private static double MinPairDistance(IReadOnlyList<PointFeature> features)
    {
        double min = double.PositiveInfinity;
        for (int i = 0; i < features.Count; i++)
        {
            for (int j = i + 1; j < features.Count; j++)
            {
                double dx = features[i].Point.X - features[j].Point.X;
                double dy = features[i].Point.Y - features[j].Point.Y;
                min = Math.Min(min, Math.Sqrt(dx * dx + dy * dy));
            }
        }
        return min;
    }

    private static void AssertImageStyle(
        PointFeature feature,
        double expectedScale,
        double expectedRotation)
    {
        var style = Assert.IsType<ImageStyle>(feature.Styles.Single());
        Assert.NotNull(style.Image);
        Assert.True(style.Image!.RasterizeSvg);
        Assert.StartsWith("svg-content://", style.Image.Source);
        Assert.Equal(expectedScale, style.SymbolScale, precision: 5);
        Assert.Equal(expectedRotation, style.SymbolRotation, precision: 3);
    }

    private static float BandScale(SymbolBand band, float value)
        => band.ScaleByValue ? band.ScaleFactor * value : band.ScaleFactor;

    private sealed class IdentityCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs)
            => IdentityCrsTransform.Instance;
    }
}
