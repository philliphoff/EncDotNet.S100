using System.Collections.Immutable;
using System.Linq;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Spec;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class FeatureNamesTests
{
    private static S122Feature Feature(
        IDictionary<string, string>? attributes = null,
        IEnumerable<S122ComplexAttribute>? complex = null)
        => new()
        {
            Id = "f1",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Attributes = (attributes ?? new Dictionary<string, string>()).ToImmutableDictionary(),
            ComplexAttributes = (complex ?? []).ToImmutableArray(),
        };

    [Fact]
    public void Surfaces_simple_name_attributes()
    {
        var feature = Feature(new Dictionary<string, string>
        {
            ["OBJNAM"] = "North Light",
            ["categoryOfLight"] = "8",
        });

        var names = FeatureNames.Enumerate(feature).ToList();

        var match = Assert.Single(names);
        Assert.Equal("OBJNAM", match.Source);
        Assert.Equal("North Light", match.Value);
    }

    [Fact]
    public void Surfaces_complex_featureName_sub_attributes()
    {
        var feature = Feature(complex: new[]
        {
            new S122ComplexAttribute
            {
                Code = "featureName",
                SubAttributes = ImmutableDictionary<string, string>.Empty
                    .Add("name", "Harbour Entrance")
                    .Add("displayName", "true"),
            },
        });

        var names = FeatureNames.Enumerate(feature).ToList();

        Assert.Contains(names, n => n.Source == "featureName.name" && n.Value == "Harbour Entrance");
        // displayName is a recognised name sub-key; "true" is non-empty so it is surfaced too.
        Assert.Contains(names, n => n.Source == "featureName.displayName");
    }

    [Fact]
    public void Ignores_non_name_attributes_and_empty_values()
    {
        var feature = Feature(
            new Dictionary<string, string>
            {
                ["objectName"] = "  ",
                ["scaleMinimum"] = "12000",
            },
            new[]
            {
                new S122ComplexAttribute
                {
                    Code = "fixedDateRange",
                    SubAttributes = ImmutableDictionary<string, string>.Empty.Add("name", "ignored"),
                },
            });

        Assert.Empty(FeatureNames.Enumerate(feature));
    }
}
