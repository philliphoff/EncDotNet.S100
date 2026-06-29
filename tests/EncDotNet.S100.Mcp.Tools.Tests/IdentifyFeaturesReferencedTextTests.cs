using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies that <see cref="IdentifyFeaturesTool.ResolveReferencedTexts"/>
/// surfaces an S-101 <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c>
/// attribute's external text to headless MCP consumers (issue #361, item 4).
/// </summary>
public class IdentifyFeaturesReferencedTextTests
{
    private static S124Feature Feature(
        ImmutableDictionary<string, string>? attributes = null,
        params S124ComplexAttribute[] complex) => new()
        {
            Id = "f1",
            FeatureType = "Light",
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((0.0, 0.0)),
            Attributes = attributes ?? ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = complex.ToImmutableArray(),
        };

    [Fact]
    public void Resolves_simple_file_reference()
    {
        var attrs = ImmutableDictionary<string, string>.Empty.Add("TXTDSC", "note.txt");
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(
            Feature(attrs), name => name == "note.txt" ? "caution text" : null);

        Assert.Single(result);
        Assert.Equal("note.txt", result[0].FileName);
        Assert.Equal("caution text", result[0].Text);
    }

    [Fact]
    public void Resolves_file_reference_in_complex_subattribute()
    {
        var complex = new S124ComplexAttribute
        {
            Code = "information",
            SubAttributes = ImmutableDictionary<string, string>.Empty.Add("fileReference", "info.txt"),
        };
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(
            Feature(complex: complex), name => "body");

        Assert.Single(result);
        Assert.Equal("info.txt", result[0].FileName);
        Assert.Equal("body", result[0].Text);
    }

    [Fact]
    public void Deduplicates_repeated_file_names()
    {
        var attrs = ImmutableDictionary<string, string>.Empty.Add("NTXTDS", "dup.txt");
        var complex = new S124ComplexAttribute
        {
            Code = "information",
            SubAttributes = ImmutableDictionary<string, string>.Empty.Add("fileReference", "dup.txt"),
        };
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(
            Feature(attrs, complex), _ => "x");

        Assert.Single(result);
    }

    [Fact]
    public void Empty_when_file_missing()
    {
        var attrs = ImmutableDictionary<string, string>.Empty.Add("TXTDSC", "gone.txt");
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(Feature(attrs), _ => null);

        Assert.Empty(result);
    }

    [Fact]
    public void Empty_when_no_resolver()
    {
        var attrs = ImmutableDictionary<string, string>.Empty.Add("TXTDSC", "note.txt");
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(Feature(attrs), resolver: null);

        Assert.Empty(result);
    }

    [Fact]
    public void Empty_when_no_file_reference_attribute()
    {
        var attrs = ImmutableDictionary<string, string>.Empty.Add("OBJNAM", "Light A");
        var result = IdentifyFeaturesTool.ResolveReferencedTexts(Feature(attrs), _ => "x");

        Assert.Empty(result);
    }
}
