using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tools;
using Mapsui.Layers;
using Mapsui.Projections;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Hover to find for a band of a colour or arrow ramp (#1433): the band's
/// area in view is tinted, sampled from the drawn grid in its own CRS.
/// </summary>
public sealed class LegendBandTintTests
{
    private static readonly ProjNetCrsTransformFactory Transforms = new();

    // Depth bands, shallowest first, as S-102's legend lists them.
    private static readonly CoverageColorScheme Depths = new()
    {
        FieldName = "depth",
        Bands =
        [
            new() { MinValue = float.NegativeInfinity, MaxValue = 10, Color = "#61B7FF", Label = "Shallow Water" },
            new() { MinValue = 10, MaxValue = float.PositiveInfinity, Color = "#C9EDFF", Label = "Deep Water" },
        ],
    };

    // S-102's legend adds the range to the band's name, so rows match by index.
    private static readonly IReadOnlyList<LegendEntry> DepthRows =
    [
        new SwatchLegendEntry("#61B7FF", "Shallow Water, < 10 m"),
        new SwatchLegendEntry("#C9EDFF", "Deep Water, ≥ 10 m"),
    ];

    private static LegendTintView ViewOf(double south, double west, double north, double east, double resolution = 0)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
        return new LegendTintView(minX, minY, maxX, maxY, resolution);
    }

    private static StyledCoverageLayer Grid(
        string crs, double originX, double originY, double spacingX, double spacingY, int rows, int cols,
        Func<int, int, float> value, CoverageColorScheme? colors = null, CoverageSymbolScheme? symbols = null, string field = "depth")
    {
        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = cols,
            OriginLongitude = originX,
            OriginLatitude = originY,
            SpacingLongitudinal = spacingX,
            SpacingLatitudinal = spacingY,
        };
        var values = new float[rows * cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                values[(r * cols) + c] = value(r, c);
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { [field] = values },
            },
            ColorScheme = colors,
            SymbolScheme = symbols,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, crs),
        };
    }

    private static GridCoverageSubLayer Fill(StyledCoverageLayer coverage, IReadOnlyList<LegendEntry> rows) => new()
    {
        LayerKey = "s102.depth",
        LayerName = "S-102",
        Plane = S98DisplayPlane.OnDemandSurface,
        Coverage = coverage,
        Viewport = new Viewport { MinLatitude = 0, MaxLatitude = 1, MinLongitude = 0, MaxLongitude = 1, WidthPixels = 1, HeightPixels = 1, ScaleDenominator = 1 },
        Legend = rows,
    };

    // The mask cells tinted, as (longitude, latitude) of their centres.
    private static List<(double Lon, double Lat)> Tinted(LegendBandTint tint)
    {
        var cellX = (tint.View.MaxX - tint.View.MinX) / tint.Width;
        var cellY = (tint.View.MaxY - tint.View.MinY) / tint.Height;
        var tinted = new List<(double, double)>();
        for (var j = 0; j < tint.Height; j++)
        {
            for (var i = 0; i < tint.Width; i++)
            {
                if (tint.IsTinted(i, j))
                    tinted.Add(SphericalMercator.ToLonLat(tint.View.MinX + ((i + 0.5) * cellX), tint.View.MaxY - ((j + 0.5) * cellY)));
            }
        }
        return tinted;
    }

    // ── Which band a row is ──────────────────────────────────────────────

    [Fact]
    public void A_row_is_the_band_at_its_index_when_the_rows_are_the_bands()
    {
        Assert.Equal(1, LegendBandTinter.BandIndex(DepthRows, "Deep Water, ≥ 10 m", ["Shallow Water", "Deep Water"]));
    }

    [Fact]
    public void Otherwise_a_row_is_the_band_labelled_as_it_and_an_unknown_row_is_none()
    {
        IReadOnlyList<LegendEntry> rows = [new SwatchLegendEntry("#000000", "Deep Water")];

        Assert.Equal(1, LegendBandTinter.BandIndex(rows, "Deep Water", ["Shallow Water", "Deep Water"]));
        Assert.Null(LegendBandTinter.BandIndex(rows, "Drying", ["Shallow Water", "Deep Water"]));
    }

    // ── The mask ─────────────────────────────────────────────────────────

    [Fact]
    public void The_mask_is_a_cell_per_few_screen_pixels_and_capped_on_the_long_side()
    {
        var (w, h) = LegendBandTinter.MaskSize(new LegendTintView(0, 0, 300, 150, 1));
        Assert.Equal((100, 50), (w, h));

        var (cw, ch) = LegendBandTinter.MaskSize(new LegendTintView(0, 0, 6000, 3000, 1));
        Assert.Equal((LegendBandTinter.MaxMaskSize, LegendBandTinter.MaxMaskSize / 2), (cw, ch));
    }

    [Fact]
    public void A_geographic_grid_tints_the_cells_in_the_band()
    {
        // 10 × 10 nodes over 0…0.9°: the western half is shallow.
        var grid = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, c) => c < 5 ? 5 : 20, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);

        Assert.True(tinter.Add(Fill(grid, DepthRows), DepthRows, "Shallow Water, < 10 m"));
        var tint = tinter.Build();

        Assert.Equal(1, tinter.Layers);
        Assert.InRange(tint.ViewFraction, 0.48, 0.52);
        Assert.All(Tinted(tint), p => Assert.True(p.Lon < 0.45, $"{p.Lon} is east of the shallow half"));
        Assert.True(tint.AreaSquareKilometres is > 5000 and < 6500, $"{tint.AreaSquareKilometres} km²");
    }

    [Fact]
    public void A_row_that_is_no_band_of_the_layer_tints_nothing()
    {
        var grid = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, _) => 5, Depths);
        var tinter = new LegendBandTinter(ViewOf(0, 0, 1, 1), Transforms);

        Assert.False(tinter.Add(Fill(grid, DepthRows), DepthRows, "Drying"));
        Assert.Equal(0, tinter.Build().TintedCells);
    }

    [Fact]
    public void A_utm_grid_tints_where_it_is_drawn_not_at_its_eastings_read_as_degrees()
    {
        // A UTM zone 30N grid off the Solent: 50 × 50 nodes at 100 m from
        // (600 000, 5 610 000); the eastern half is shallow.
        var grid = Grid("EPSG:32630", 600_000, 5_610_000, 100, 100, 50, 50, (_, c) => c >= 25 ? 5 : 20, Depths);
        var toWgs84 = Transforms.Create("EPSG:32630", "EPSG:4326");
        var (west, south) = toWgs84.Transform(600_000, 5_610_000);
        var (east, north) = toWgs84.Transform(604_900, 5_614_900);
        var (midLon, _) = toWgs84.Transform(602_450, 5_612_450);
        var tinter = new LegendBandTinter(ViewOf(south, west, north, east), Transforms);

        tinter.Add(Fill(grid, DepthRows), DepthRows, "Shallow Water, < 10 m");
        var tint = tinter.Build();

        Assert.InRange(tint.ViewFraction, 0.4, 0.6);
        Assert.All(Tinted(tint), p => Assert.True(p.Lon > midLon - 0.002, $"{p.Lon} is west of the shallow half ({midLon})"));
    }

    [Fact]
    public void A_grid_in_a_0_to_360_frame_tints_west_of_greenwich()
    {
        // Nodes at 200…200.9° east, i.e. 160…159.1° west.
        var grid = Grid("EPSG:4326", 200, 0, 0.1, 0.1, 10, 10, (_, _) => 5, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -160.05, 0.95, -159.15), Transforms);

        tinter.Add(Fill(grid, DepthRows), DepthRows, "Shallow Water, < 10 m");

        Assert.InRange(tinter.Build().ViewFraction, 0.95, 1.0);
    }

    [Fact]
    public void The_top_fill_decides_a_cell_as_it_hides_the_fills_under_it()
    {
        // The top grid covers the western half and is deep; the one under it
        // covers everything and is shallow: only its eastern half shows.
        var top = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 5, (_, _) => 20, Depths);
        var under = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, _) => 5, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);

        tinter.Add(Fill(top, DepthRows), DepthRows, "Shallow Water, < 10 m");
        tinter.Add(Fill(under, DepthRows), DepthRows, "Shallow Water, < 10 m");
        var tint = tinter.Build();

        Assert.InRange(tint.ViewFraction, 0.48, 0.52);
        Assert.All(Tinted(tint), p => Assert.True(p.Lon > 0.44, $"{p.Lon} is under the deep top grid"));
    }

    [Fact]
    public void No_data_lets_the_fill_under_it_show()
    {
        var top = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, _) => float.NaN, Depths);
        var under = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, _) => 5, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);

        tinter.Add(Fill(top, DepthRows), DepthRows, "Shallow Water, < 10 m");
        tinter.Add(Fill(under, DepthRows), DepthRows, "Shallow Water, < 10 m");

        Assert.InRange(tinter.Build().ViewFraction, 0.95, 1.0);
    }

    [Fact]
    public void An_arrow_band_tints_every_cell_whose_speed_is_in_it()
    {
        var speeds = new CoverageSymbolScheme
        {
            ValueFieldName = "speed",
            RotationFieldName = "direction",
            Bands =
            [
                new() { MinValue = 0, MaxValue = 1, SymbolRef = "SCAROW01", Label = "< 1 kn" },
                new() { MinValue = 1, MaxValue = 100, SymbolRef = "SCAROW02", Label = "≥ 1 kn" },
            ],
        };
        // The southern row (row 0) runs fast, the rest slow.
        var grid = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (r, _) => r == 0 ? 2 : 0.5f, symbols: speeds, field: "speed");
        IReadOnlyList<LegendEntry> rows = [new SymbolLegendEntry("<svg/>", "< 1 kn"), new SymbolLegendEntry("<svg/>", "≥ 1 kn")];
        var arrows = new ArrowCoverageSubLayer
        {
            LayerKey = "s111.arrows",
            LayerName = "S-111",
            Plane = S98DisplayPlane.DynamicArrows,
            Coverage = grid,
            Viewport = new Viewport { MinLatitude = 0, MaxLatitude = 1, MinLongitude = 0, MaxLongitude = 1, WidthPixels = 1, HeightPixels = 1, ScaleDenominator = 1 },
            SymbolProvider = _ => null,
            FallbackExtent = new GeographicBounds(0, 0, 1, 1),
            Legend = rows,
        };
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);

        Assert.True(tinter.Add(arrows, rows, "≥ 1 kn"));
        var tint = tinter.Build();

        Assert.InRange(tint.ViewFraction, 0.08, 0.12);
        Assert.All(Tinted(tint), p => Assert.True(p.Lat < 0.06, $"{p.Lat} is north of the fast row"));
    }

    // ── The overlay and MCP ──────────────────────────────────────────────

    [Fact]
    public void The_overlay_draws_the_tint_as_one_raster_over_the_view_and_clears_without_one()
    {
        var grid = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, c) => c < 5 ? 5 : 20, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);
        tinter.Add(Fill(grid, DepthRows), DepthRows, "Shallow Water, < 10 m");
        var tint = tinter.Build();
        var layer = new MemoryLayer();

        LegendFindOverlayController.UpdateTint(layer, tint, PickHighlightAppearance.Default);
        var raster = Assert.IsType<RasterFeature>(Assert.Single(layer.Features));
        Assert.Equal(tint.View.MinX, raster.Extent!.MinX, 6);
        Assert.Equal(tint.View.MaxY, raster.Extent.MaxY, 6);

        LegendFindOverlayController.UpdateTint(layer, null, PickHighlightAppearance.Default);
        Assert.Empty(layer.Features);
    }

    [Fact]
    public void The_overlay_edges_the_band_in_accent_cased_in_white_and_leaves_the_rest_clear()
    {
        // One tinted cell in the middle of a 5 × 5 mask.
        var mask = new bool[25];
        mask[12] = true;
        var tint = new LegendBandTint(ViewOf(0, 0, 1, 1), 5, 5, mask) { TintedCells = 1 };
        var layer = new MemoryLayer();

        LegendFindOverlayController.UpdateTint(layer, tint, PickHighlightAppearance.Default);
        var raster = Assert.IsType<RasterFeature>(Assert.Single(layer.Features));
        using var bitmap = SKBitmap.Decode(raster.Raster!.Data);

        var accent = PickHighlightOverlayLayer.DefaultAccent;
        var centre = bitmap.GetPixel(2, 2);
        Assert.Equal((accent.R, accent.G, accent.B, (byte)255), (centre.Red, centre.Green, centre.Blue, centre.Alpha));
        var casing = bitmap.GetPixel(2, 1);
        Assert.True(casing.Red > 200 && casing.Green > 200, $"casing {casing}");
        Assert.True(casing.Alpha > 0);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void The_find_dto_reports_the_tinted_area()
    {
        var grid = Grid("EPSG:4326", 0, 0, 0.1, 0.1, 10, 10, (_, c) => c < 5 ? 5 : 20, Depths);
        var tinter = new LegendBandTinter(ViewOf(-0.05, -0.05, 0.95, 0.95), Transforms);
        tinter.Add(Fill(grid, DepthRows), DepthRows, "Shallow Water, < 10 m");
        var find = new LegendFeatureFind("S-102", "Shallow Water, < 10 m", LegendFindKind.Band, [], tinter.Layers) { Tint = tinter.Build() };

        var dto = LegendFindDto.From(find);

        Assert.Equal("band", dto.Kind);
        Assert.Equal(1, dto.Matched);
        var tint = Assert.IsType<LegendFindTintDto>(dto.Tint);
        Assert.InRange(tint.ViewPercent, 48, 52);
        Assert.InRange(tint.West!.Value, -0.05, 0);
        Assert.InRange(tint.East!.Value, 0.4, 0.45);
        Assert.True(tint.AreaSquareKilometres > 0);
    }
}
