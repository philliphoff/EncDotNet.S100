using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Cli.Tests;

public class DisplayListJsonWriterTests
{
    private static readonly GeoPosition SW = new(50, -1);
    private static readonly GeoPosition S = new(50, -0.9);
    private static readonly GeoPosition SE = new(50, -0.8);
    private static readonly GeoPosition NE = new(50.1, -0.8);
    private static readonly GeoPosition N = new(50.1, -0.9);
    private static readonly GeoPosition NW = new(50.1, -1);

    // "west" and "east" share the side S–N; "light" has no line geometry of
    // its own, the situation for a Part 9A augmented line (e.g. a light sector).
    private sealed class Squares : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "west" => new() { Type = GeometryType.Surface, Coordinates = [SW, S, N, NW, SW] },
            "east" => new() { Type = GeometryType.Surface, Coordinates = [S, SE, NE, N, S] },
            "buoy" => new() { Type = GeometryType.Point, Coordinates = [S] },
            _ => null,
        };
    }

    private static VectorPortrayalResult ResultFor(params DrawingInstruction[] instructions) =>
        new()
        {
            SubLayers =
            [
                new VectorSubLayer
                {
                    LayerKey = "s101.linework",
                    LayerName = "Linework",
                    Instructions = instructions,
                    Plane = S98DisplayPlane.BaseChartUnder,
                },
            ],
            Palette = new ColorPalette("test", new Dictionary<string, string> { ["CHGRD"] = "#808080" }),
            GeometryProvider = new Squares(),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = "ds",
            Info = "test",
        };

    private static JsonElement Root(params DrawingInstruction[] instructions)
    {
        using var doc = JsonDocument.Parse(DisplayListJsonWriter.Serialize(ResultFor(instructions), "ds", "day"));
        return doc.RootElement.Clone();
    }

    private static List<(double Lon, double Lat)> Coordinates(JsonElement op) =>
        op.GetProperty("coordinates").EnumerateArray()
            .Select(c => (c[0].GetDouble(), c[1].GetDouble()))
            .ToList();

    private static LineInstruction Dashed(string feature, int priority) => new()
    {
        FeatureReference = feature,
        LineColor = "CHGRD",
        LineWidth = 0.32,
        Dashes = [(0, 3.6)],
        DashIntervalLength = 5.4,
        DrawingPriority = priority,
        Suppression = true,
    };

    [Fact]
    public void Document_carries_the_schema_version_and_size_profile()
    {
        var root = Root(Dashed("west", 3));

        Assert.Equal(DisplayListJsonWriter.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("EPSG:4326", root.GetProperty("crs").GetString());
        Assert.Equal(0.32, root.GetProperty("sizeProfile").GetProperty("millimetresPerPixel").GetDouble());
        Assert.Equal(1, root.GetProperty("instructionCount").GetInt32());
        Assert.Equal(root.GetProperty("opCount").GetInt32(), root.GetProperty("ops").GetArrayLength());
    }

    [Fact]
    public void AugmentedLine_IsDrawnFromItsCoordinatesOverride()
    {
        var line = new LineInstruction
        {
            FeatureReference = "light",
            LineStyleReference = "SECTR1",
            CoordinatesOverride = [new GeoPosition(10.0, 20.0), new GeoPosition(11.0, 21.0), new GeoPosition(12.0, 22.0)],
        };

        var op = Assert.Single(Root(line).GetProperty("ops").EnumerateArray());

        Assert.Equal("line", op.GetProperty("kind").GetString());
        Assert.Equal("SECTR1", op.GetProperty("lineStyle").GetString());
        // GeoJSON order: longitude, latitude.
        Assert.Equal([(20.0, 10.0), (21.0, 11.0), (22.0, 12.0)], Coordinates(op));
    }

    [Fact]
    public void Line_WithNoGeometry_IsNotDrawn()
    {
        var line = new LineInstruction { FeatureReference = "nothing", LineStyleReference = "CSTLN" };

        var root = Root(line);

        Assert.Equal(1, root.GetProperty("instructionCount").GetInt32());
        Assert.Equal(0, root.GetProperty("ops").GetArrayLength());
    }

    [Fact]
    public void SharedDashedBoundary_IsHandedOutOnce_ByTheLineDrawnLast()
    {
        var ops = Root(Dashed("west", 3), Dashed("east", 6)).GetProperty("ops").EnumerateArray().ToList();

        // Line suppression (S-100 Part 9) as the renderer applies it: west
        // keeps three sides, east all four, so S–N is stroked once.
        var west = Assert.Single(ops, o => o.GetProperty("feature").GetString() == "west");
        var east = Assert.Single(ops, o => o.GetProperty("feature").GetString() == "east");
        Assert.Equal(4, Coordinates(west).Count);
        Assert.Equal(5, Coordinates(east).Count);
        Assert.Equal(3, west.GetProperty("drawingPriority").GetInt32());
        Assert.Equal(6, east.GetProperty("drawingPriority").GetInt32());

        // Dashes resolved to display pixels at 0.32 mm/px, colours to RGBA.
        Assert.Equal("#808080FF", east.GetProperty("color").GetString());
        Assert.Equal("CHGRD", east.GetProperty("colorToken").GetString());
        Assert.Equal([11.25, 5.625], east.GetProperty("dashArrayPx").EnumerateArray().Select(d => d.GetDouble()));
    }

    [Fact]
    public void PointAndText_CarryAnchorPlacementAndResolvedStyle()
    {
        var point = new PointInstruction
        {
            FeatureReference = "buoy",
            SymbolReference = "BOYCAN01",
            Rotation = 45,
            DrawingPriority = 12,
            ViewingGroup = 27010,
            AdditionalViewingGroups = [90020],
            ScaleMinimum = 22000,
        };
        var text = new TextInstruction
        {
            FeatureReference = "buoy",
            Text = "No 3",
            FontColor = "CHGRD",
            FontSize = 10,
            OffsetX = 3.2,
            OffsetY = -1.6,
            HorizontalAlignment = TextHorizontalAlignment.Start,
            DrawingPriority = 24,
        };

        var ops = Root(point, text).GetProperty("ops").EnumerateArray().ToList();

        var p = Assert.Single(ops, o => o.GetProperty("kind").GetString() == "point");
        Assert.Equal("BOYCAN01", p.GetProperty("symbol").GetString());
        Assert.Equal([-0.9, 50.0], p.GetProperty("anchor").EnumerateArray().Select(c => c.GetDouble()));
        Assert.Equal("Point", p.GetProperty("placement").GetProperty("kind").GetString());
        Assert.Equal(45, p.GetProperty("rotation").GetDouble());
        Assert.Equal("Portrayal", p.GetProperty("rotationCrs").GetString());
        Assert.Equal([27010, 90020], p.GetProperty("viewingGroups").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(22000, p.GetProperty("scaleMinimum").GetDouble());
        Assert.Equal(12, p.GetProperty("drawingPriority").GetInt32());

        var t = Assert.Single(ops, o => o.GetProperty("kind").GetString() == "text");
        Assert.Equal("No 3", t.GetProperty("text").GetString());
        Assert.Equal(10, t.GetProperty("fontSizePx").GetDouble());
        Assert.Equal("Start", t.GetProperty("horizontalAlignment").GetString());
        Assert.Equal("Medium", t.GetProperty("fontWeight").GetString());
        // Offsets in display pixels (mm / 0.32), as the scene draws them.
        Assert.Equal([10.0, -5.0], t.GetProperty("offsetPx").EnumerateArray().Select(c => c.GetDouble()));
        Assert.Equal("#808080FF", t.GetProperty("color").GetString());
    }
}
