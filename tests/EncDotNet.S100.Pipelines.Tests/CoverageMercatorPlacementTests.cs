using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A geographic coverage drawn into a Web Mercator tile puts each grid row at
/// its own latitude, not where a linear stretch between the grid's projected
/// corners would put it (issue #1691: a 0–55°N GFS-Wave fill drew 3.5° north
/// in <c>tiles serve</c>).
/// </summary>
public class CoverageMercatorPlacementTests
{
    private const double Edge = 25.0;

    [Theory]
    [InlineData(4, 1)]
    [InlineData(6, 1)]
    [InlineData(6, 2)]
    [InlineData(8, 1)]
    public void Tile_FillEdge_SitsAtTheGridLatitude_NorthernGrid(int zoom, int pixelRatio) =>
        AssertEdgePlacement(south: 0, north: 55, filledSouthOfEdge: true, edge: Edge, zoom, pixelRatio);

    [Theory]
    [InlineData(4, 1)]
    [InlineData(6, 2)]
    public void Tile_FillEdge_SitsAtTheGridLatitude_SouthernGrid(int zoom, int pixelRatio) =>
        AssertEdgePlacement(south: -55, north: 0, filledSouthOfEdge: false, edge: -Edge, zoom, pixelRatio);

    private static void AssertEdgePlacement(
        double south, double north, bool filledSouthOfEdge, double edge, int zoom, int pixelRatio)
    {
        const double west = -90, east = -70;
        var layer = new CoverageCompositeLayer(
            MakeLayer(south, north, west, east, edge, filledSouthOfEdge), west, east, south, north);

        // The tile holding the edge at 80°W.
        var (edgeX, edgeY) = WebMercator.FromLonLat(-80, edge);
        double tileSize = XyzTileGrid.TileWorldSize(zoom);
        int tileX = (int)Math.Floor((edgeX + XyzTileGrid.Extent) / tileSize);
        int tileY = (int)Math.Floor((XyzTileGrid.Extent - edgeY) / tileSize);
        var (minX, minY, maxX, maxY) = XyzTileGrid.TileWorldBounds(zoom, tileX, tileY);
        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY, clampLatitude: false);
        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = XyzTileGrid.TileSize,
            HeightPixels = XyzTileGrid.TileSize,
            ScaleDenominator = XyzTileGrid.ScaleDenominator(zoom, edge),
        };

        int size = XyzTileGrid.TileSize * pixelRatio;
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(pixelRatio);
            layer.Draw(canvas, viewport);
        }

        // Where the fill's alpha crosses half, down the middle column.
        int column = size / 2;
        double? found = null;
        for (int row = 1; row < size && found is null; row++)
        {
            int above = bitmap.GetPixel(column, row - 1).Alpha;
            int below = bitmap.GetPixel(column, row).Alpha;
            if ((above < 128) != (below < 128))
                found = row - 1 + 0.5 + ((128.0 - above) / (below - above));
        }

        double expected = (maxY - edgeY) / XyzTileGrid.Resolution(zoom) * pixelRatio;
        Assert.True(found is not null, $"No fill edge in tile {zoom}/{tileX}/{tileY}; expected one at row {expected:F1}.");
        Assert.True(
            Math.Abs(found.Value - expected) <= 1.5,
            $"The fill edge is at row {found:F1} of tile {zoom}/{tileX}/{tileY}; {edge}° is at row {expected:F1}.");
    }

    /// <summary>
    /// A 1° grid of cell centres spanning <paramref name="south"/>–<paramref name="north"/>,
    /// filled on one side of <paramref name="edge"/> and no-data on the other.
    /// </summary>
    private static StyledCoverageLayer MakeLayer(
        double south, double north, double west, double east, double edge, bool filledSouthOfEdge)
    {
        var metadata = new GridMetadata
        {
            NumRows = (int)(north - south),
            NumColumns = (int)(east - west),
            OriginLatitude = south + 0.5,
            OriginLongitude = west + 0.5,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };

        var values = new float[metadata.NumRows * metadata.NumColumns];
        for (int row = 0; row < metadata.NumRows; row++)
        {
            double latitude = metadata.OriginLatitude + row;
            bool filled = (latitude < edge) == filledSouthOfEdge;
            for (int col = 0; col < metadata.NumColumns; col++)
                values[(row * metadata.NumColumns) + col] = filled ? 5f : float.NaN;
        }

        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { ["height"] = values },
            },
            ColorScheme = new CoverageColorScheme
            {
                FieldName = "height",
                Bands = [new ColorBand { MinValue = 0f, MaxValue = 10f, Color = "#6BC5FF" }],
            },
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
    }
}
