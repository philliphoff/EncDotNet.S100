using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="CoverageContours"/>: value-based
/// <see cref="ContourStyle"/>s traced through a sampled coverage into
/// <see cref="Isoline"/>s (#1072).
/// </summary>
public sealed class CoverageContoursTests
{
    [Fact]
    public void Traces_only_the_levels_the_values_reach_in_contour_order()
    {
        // 3 × 5 grid at 1° from (10°S, 20°E), rising 10 per column: 0 to 40.
        var layer = Layer(3, 5, (_, c) => 10f * c);

        var lines = CoverageContours.Trace(layer, "v", [Contour(-5), Contour(25), Contour(15), Contour(45)]);

        Assert.Equal([25.0, 15.0], lines.Select(l => l.Level));
        Assert.Equal(["25", "15"], lines.Select(l => l.Label));

        // 15 lies half way between columns 1 and 2: 21.5°E, from 10°S to 8°S.
        var fifteen = lines[1].MercatorCoordinates;
        var (x, _) = WebMercator.FromLonLat(21.5, 0);
        var (_, south) = WebMercator.FromLonLat(0, -10);
        var (_, north) = WebMercator.FromLonLat(0, -8);
        Assert.All(Enumerable.Range(0, fifteen.Length / 2), i => Assert.Equal(x, fifteen[2 * i], 6));
        var ys = Enumerable.Range(0, fifteen.Length / 2).Select(i => fifteen[2 * i + 1]).ToArray();
        Assert.Equal(south, ys.Min(), 6);
        Assert.Equal(north, ys.Max(), 6);
    }

    [Fact]
    public void Labels_come_from_the_formatter_and_unlabelled_contours_get_none()
    {
        var layer = Layer(2, 3, (_, c) => 10f * c);

        var lines = CoverageContours.Trace(
            layer, "v", [Contour(5, showLabel: true), Contour(15, showLabel: false)], c => $"L{c.Value}");

        Assert.Equal(["L5", ""], lines.Select(l => l.Label));
    }

    [Fact]
    public void No_data_breaks_the_lines_and_an_all_no_data_sample_has_none()
    {
        // Every cell west of column 2 touches the hole, so 5 has no line; 25,
        // between columns 2 and 3, does.
        var holed = Layer(3, 4, (r, c) => r == 1 && c == 1 ? float.NaN : 10f * c);
        var empty = Layer(2, 2, (_, _) => -9999f, noData: -9999f);

        Assert.Empty(CoverageContours.Trace(holed, "v", [Contour(5)]));
        Assert.NotEmpty(CoverageContours.Trace(holed, "v", [Contour(25)]));
        Assert.Empty(CoverageContours.Trace(empty, "v", [Contour(5)]));
        Assert.Empty(CoverageContours.Trace(holed, "v", []));
    }

    [Fact]
    public void A_native_transform_is_applied_before_projection()
    {
        var layer = Layer(2, 3, (_, c) => 10f * c);

        var lines = CoverageContours.Trace(layer, "v", [Contour(5)], nativeToWgs84: (x, y) => (x + 100, y));

        // The 5 contour is at native x 20.5, which the transform moves to 120.5°E.
        Assert.Equal(WebMercator.FromLonLat(120.5, 0).X, lines.Single().MercatorCoordinates[0], 6);
    }

    [Fact]
    public void An_unlabelled_isoline_gets_no_label_anchors()
    {
        var (minX, minY) = WebMercator.FromLonLat(0, 0);
        var (maxX, maxY) = WebMercator.FromLonLat(1, 1);
        double midX = (minX + maxX) / 2;
        using var scene = new IsolineScene(
            [new IsolinePolyline("1012", [minX, minY, minX, maxY]), new IsolinePolyline("", [midX, minY, midX, maxY])],
            new IsolineAppearance { Ink = SKColors.Black, LabelHalo = SKColors.White, DashPattern = [] });

        var toScreen = SKMatrix.CreateScale(1e-3f, 1e-3f);
        var labels = scene.LabelAnchors(toScreen).Select(a => a.Label).Distinct();

        Assert.Equal(["1012"], labels);
    }

    private static ContourStyle Contour(double value, bool showLabel = true) => new()
    {
        Value = value,
        Unit = "1",
        LineWidth = 1f,
        Color = "#000000",
        ShowLabel = showLabel,
    };

    private static StyledCoverageLayer Layer(int rows, int columns, Func<int, int, float> value, float noData = float.NaN)
    {
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                values[r * columns + c] = value(r, c);
        }

        var metadata = new GridMetadata
        {
            NumRows = rows,
            NumColumns = columns,
            OriginLatitude = -10,
            OriginLongitude = 20,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = metadata,
                Values = new Dictionary<string, float[]> { ["v"] = values },
            },
            NoDataValue = noData,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
    }
}
