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
    // A geometry provider that has no natural geometry for the feature — the
    // situation for a Part 9A augmented line whose parent feature is, e.g., a
    // point Light. The line is still drawn from its CoordinatesOverride.
    private sealed class NullGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    private static VectorPortrayalResult ResultFor(DrawingInstruction instruction) =>
        new()
        {
            SubLayers =
            [
                new VectorSubLayer
                {
                    LayerKey = "s101.linework",
                    LayerName = "Linework",
                    Instructions = [instruction],
                    Plane = S98DisplayPlane.BaseChartUnder,
                },
            ],
            Palette = new ColorPalette("test", new Dictionary<string, string>()),
            GeometryProvider = new NullGeometryProvider(),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = "ds",
            Info = "test",
        };

    private static JsonElement FirstInstruction(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("instructions")[0].Clone();
    }

    [Fact]
    public void AugmentedLine_SummarisesCoordinatesOverride_NotNullFeatureGeometry()
    {
        var line = new LineInstruction
        {
            FeatureReference = "light-1",
            LineStyleReference = "SECTR1",
            CoordinatesOverride =
            [
                new GeoPosition(10.0, 20.0),
                new GeoPosition(11.0, 21.0),
                new GeoPosition(12.0, 22.0),
            ],
        };

        var geometry = FirstInstruction(DisplayListJsonWriter.Serialize(ResultFor(line), "ds", "Day"))
            .GetProperty("geometry");

        // Without the fix the feature geometry is null, so the summary would be
        // null; the override must be reflected instead.
        Assert.Equal(JsonValueKind.Object, geometry.ValueKind);
        Assert.Equal("Curve", geometry.GetProperty("type").GetString());
        Assert.Equal(3, geometry.GetProperty("vertexCount").GetInt32());
        var anchor = geometry.GetProperty("anchor");
        Assert.Equal(10.0, anchor[0].GetDouble());
        Assert.Equal(20.0, anchor[1].GetDouble());
    }

    [Fact]
    public void Line_WithoutCoordinatesOverride_AndNoFeatureGeometry_HasNullGeometry()
    {
        var line = new LineInstruction
        {
            FeatureReference = "coastline-1",
            LineStyleReference = "CSTLN",
        };

        // A null geometry summary is omitted from the JSON (WhenWritingNull), so
        // the instruction carries no "geometry" property at all.
        var instruction = FirstInstruction(DisplayListJsonWriter.Serialize(ResultFor(line), "ds", "Day"));

        Assert.False(instruction.TryGetProperty("geometry", out _));
    }
}
