using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Pins the wire shape of <see cref="DatasetId"/>: it serialises as a bare
/// JSON string so that the identifier round-trips symmetrically with the
/// plain-string <c>datasetId</c> arguments every MCP tool accepts.
/// </summary>
public class DatasetIdJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Write_EmitsBareString()
    {
        var json = JsonSerializer.Serialize(new DatasetId("synth-1"), Options);

        Assert.Equal("\"synth-1\"", json);
    }

    [Fact]
    public void Read_AcceptsBareString()
    {
        var id = JsonSerializer.Deserialize<DatasetId>("\"synth-1\"", Options);

        Assert.Equal("synth-1", id.Value);
    }

    [Fact]
    public void Read_AcceptsLegacyWrappedObject()
    {
        var id = JsonSerializer.Deserialize<DatasetId>("{\"value\":\"synth-1\"}", Options);

        Assert.Equal("synth-1", id.Value);
    }

    [Fact]
    public void RoundTrip_FromOutputStringBindsBackAsInput()
    {
        // An agent reads an id off a result (bare string) and feeds it back
        // into a tool argument; both directions must agree.
        var emitted = JsonSerializer.Serialize(new DatasetId("warn-here"), Options);
        var rebound = JsonSerializer.Deserialize<DatasetId>(emitted, Options);

        Assert.Equal("warn-here", rebound.Value);
    }

    [Fact]
    public void Write_AsDictionaryKey_EmitsBareStringPropertyName()
    {
        var map = new Dictionary<DatasetId, int> { [new DatasetId("k1")] = 7 };

        var json = JsonSerializer.Serialize(map, Options);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(7, node["k1"]!.GetValue<int>());
    }

    [Fact]
    public void Read_RejectsObjectMissingValue()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<DatasetId>("{\"other\":\"x\"}", Options));
    }
}
