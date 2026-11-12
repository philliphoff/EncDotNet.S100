using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Where a neighbouring S-102 cell carries on (S-98 rule R-102-102-A), the
/// Mapsui coverage raster carries its edge colour on under the neighbour, so
/// the linear-filter fades of the two cells don't leave a light seam.
/// </summary>
public sealed class CoverageNeighbourEdgeTests
{
    // Nodes at 20–25° E, 10–6° S, 1° apart; depths in the western three
    // columns (20–22° E).
    private const int Rows = 5, Columns = 6;

    [Fact]
    public void Without_neighbours_the_edge_stays_where_the_data_ends()
    {
        var pixels = Render(neighbours: null);

        Assert.InRange(DrawnColumns(pixels, row: 2), 3, 4);
    }

    [Fact]
    public void Where_a_neighbour_carries_on_the_edge_goes_two_pixels_further()
    {
        int alone = DrawnColumns(Render(neighbours: null), row: 2);
        var pixels = Render([new Area(_ => true)]);

        Assert.Equal(alone + 2, DrawnColumns(pixels, row: 2));
        Assert.Equal(pixels[2, 0], pixels[2, alone + 1]);
    }

    [Fact]
    public void The_edge_moves_only_where_the_neighbour_has_data()
    {
        // The neighbour has data north of 7.5° S only: rows 3 and 4.
        int alone = DrawnColumns(Render(neighbours: null), row: 0);
        var pixels = Render([new Area(lat => lat > -7.5)]);
        int north = pixels.GetLength(0) - 1;

        Assert.Equal(alone, DrawnColumns(pixels, row: 0));
        Assert.Equal(alone + 2, DrawnColumns(pixels, row: north));
    }

    /// <summary>The raster's pixels, row 0 south, as packed colours: one pixel a node, give or take the padding.</summary>
    private static uint[,] Render(IReadOnlyList<ICoverageDataArea>? neighbours)
    {
        var values = new float[Rows * Columns];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                values[(r * Columns) + c] = c < 3 ? 10f : float.NaN;

        var grid = new GridMetadata
        {
            NumRows = Rows,
            NumColumns = Columns,
            OriginLatitude = -10,
            OriginLongitude = 20,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        var layer = new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = grid,
                Values = new Dictionary<string, float[]> { ["depth"] = values },
            },
            ColorScheme = new CoverageColorScheme
            {
                FieldName = "depth",
                Bands = [new ColorBand { MinValue = 0f, MaxValue = 100f, Color = "#4080C0" }],
            },
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(grid, "EPSG:4326"),
        };
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory()) { NeighbourDataAreas = neighbours };
        var viewport = new Viewport
        {
            MinLatitude = -10,
            MaxLatitude = -6,
            MinLongitude = 20,
            MaxLongitude = 25,
            WidthPixels = 100,
            HeightPixels = 100,
            ScaleDenominator = 1_000_000,
        };

        var memory = Assert.IsType<MemoryLayer>(renderer.Render(layer, viewport));
        var feature = Assert.Single(memory.Features.OfType<RasterFeature>());
        using var bitmap = SKBitmap.Decode(feature.Raster!.Data);
        int height = bitmap.Height, width = bitmap.Width;
        var pixels = new uint[height, width];
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
                pixels[r, c] = (uint)bitmap.GetPixel(c, height - 1 - r);
        return pixels;
    }

    private static int DrawnColumns(uint[,] pixels, int row) =>
        Enumerable.Range(0, pixels.GetLength(1)).Count(c => (pixels[row, c] >> 24) != 0);

    /// <summary>A neighbour with data wherever <paramref name="hasData"/> says, by latitude.</summary>
    private sealed class Area(Func<double, bool> hasData) : ICoverageDataArea
    {
        public bool HasData(double mercatorX, double mercatorY) =>
            hasData(WebMercator.ToLonLat(mercatorX, mercatorY).Latitude);
    }
}
