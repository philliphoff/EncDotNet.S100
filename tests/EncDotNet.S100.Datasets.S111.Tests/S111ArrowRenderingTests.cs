using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
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
        _catalogue.SwitchPalette(PaletteType.Day);

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
    public void Day_and_Night_palettes_produce_different_resolved_SVG()
    {
        var renderer = CreateRenderer();

        _catalogue.SwitchPalette(PaletteType.Day);
        renderer.Palette = _catalogue.ActivePalette;
        var daySvg = renderer.GetResolvedSvg("SCAROW01");

        _catalogue.SwitchPalette(PaletteType.Night);
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
        const string ValueField = "surfaceCurrentSpeed";
        const string RotationField = "surfaceCurrentDirection";

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
                [ValueField] = new float[] { 0.25f, 3.5f, 15.0f },
                [RotationField] = new float[] { 0f, 90f, 180f },
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
        var result = renderer.Render(layer, BuildViewport());

        var memory = Assert.IsType<MemoryLayer>(result);
        var features = (memory.Features ?? Enumerable.Empty<Mapsui.IFeature>())
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
        var result = renderer.Render(layer, BuildViewport());

        Assert.Null(result);
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

    private static Viewport BuildViewport() => new()
    {
        MinLatitude = -1.0,
        MaxLatitude = 1.0,
        MinLongitude = -1.0,
        MaxLongitude = 4.0,
        WidthPixels = 100,
        HeightPixels = 100,
        ScaleDenominator = 1_000_000,
    };

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
