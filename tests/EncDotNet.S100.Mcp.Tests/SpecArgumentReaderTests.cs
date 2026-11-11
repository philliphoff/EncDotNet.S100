using System.Text.Json;

namespace EncDotNet.S100.Mcp.Tests;

public class SpecArgumentReaderTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("\"S-101\"", "S-101")]
    [InlineData("\"S-124/1.5.0\"", "S-124/1.5.0")]
    [InlineData("{\"name\":\"S-101\"}", "S-101")]
    [InlineData("{\"name\":\"S-101\",\"edition\":null}", "S-101")]
    [InlineData("{\"name\":\"S-124\",\"edition\":{\"major\":1,\"minor\":5,\"clarification\":0}}", "S-124/1.5.0")]
    [InlineData("{\"Name\":\"S-124\",\"Edition\":{\"Major\":1,\"Minor\":5}}", "S-124/1.5.0")]
    [InlineData("{\"name\":\"S-102\",\"edition\":\"2.1.0\"}", "S-102/2.1.0")]
    public void ReadText_accepts_strings_and_spec_objects(string json, string expected)
    {
        Assert.Equal(expected, SpecArgumentReader.ReadText(Json(json)));
    }

    [Fact]
    public void ReadText_treats_an_all_zero_edition_as_edition_agnostic()
    {
        // Results serialise an edition-agnostic SpecRef with a 0.0.0 edition.
        Assert.Equal(
            "S-101",
            SpecArgumentReader.ReadText(Json("{\"name\":\"S-101\",\"edition\":{\"major\":0,\"minor\":0,\"clarification\":0}}")));
    }

    [Fact]
    public void ReadText_returns_null_when_absent_or_null()
    {
        Assert.Null(SpecArgumentReader.ReadText(null));
        Assert.Null(SpecArgumentReader.ReadText(Json("null")));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[\"S-101\"]")]
    [InlineData("{}")]
    [InlineData("{\"name\":7}")]
    [InlineData("{\"name\":\"S-101\",\"edition\":{\"major\":\"one\"}}")]
    [InlineData("{\"name\":\"S-101\",\"edition\":{\"major\":-1}}")]
    [InlineData("{\"name\":\"S-101\",\"edition\":3}")]
    public void ReadText_rejects_other_shapes_naming_the_spec_parameter(string json)
    {
        var ex = Assert.Throws<ArgumentException>(() => SpecArgumentReader.ReadText(Json(json)));
        Assert.Equal("spec", ex.ParamName);
    }
}
