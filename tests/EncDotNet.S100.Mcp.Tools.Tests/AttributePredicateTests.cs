using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class AttributePredicateTests
{
    private static IS100Feature FeatureWith(params (string Key, string Value)[] attributes)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in attributes)
        {
            builder[key] = value;
        }

        return new S122Feature
        {
            Id = "f1",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Attributes = builder.ToImmutable(),
            ComplexAttributes = ImmutableArray<S122ComplexAttribute>.Empty,
        };
    }

    [Fact]
    public void Object_map_parses_to_equality_predicates()
    {
        var predicates = AttributePredicateJsonReader.Parse(
            "{\"categoryOfLateralMark\":\"1\",\"objectName\":\"Foo\"}");

        Assert.Equal(2, predicates.Length);
        Assert.All(predicates, p => Assert.Equal(AttributeOperator.Eq, p.Op));
        Assert.Contains(predicates, p => p.Attribute == "categoryOfLateralMark" && p.Value == "1");
        Assert.Contains(predicates, p => p.Attribute == "objectName" && p.Value == "Foo");
    }

    [Fact]
    public void Object_map_coerces_numbers_and_booleans_to_strings()
    {
        var predicates = AttributePredicateJsonReader.Parse(
            "{\"valueOfDepth\":12.5,\"flag\":true}");

        Assert.Contains(predicates, p => p.Attribute == "valueOfDepth" && p.Value == "12.5");
        Assert.Contains(predicates, p => p.Attribute == "flag" && p.Value == "true");
    }

    [Fact]
    public void Array_form_parses_explicit_operators()
    {
        var predicates = AttributePredicateJsonReader.Parse(
            "[{\"attribute\":\"valueOfDepth\",\"op\":\"ge\",\"value\":\"10\"}," +
            "{\"attribute\":\"objectName\",\"op\":\"exists\"}]");

        Assert.Equal(2, predicates.Length);
        Assert.Equal(AttributeOperator.Ge, predicates[0].Op);
        Assert.Equal("10", predicates[0].Value);
        Assert.Equal(AttributeOperator.Exists, predicates[1].Op);
        Assert.Null(predicates[1].Value);
    }

    [Theory]
    [InlineData("=", AttributeOperator.Eq)]
    [InlineData(">=", AttributeOperator.Ge)]
    [InlineData("STARTSWITH", AttributeOperator.StartsWith)]
    [InlineData("not_exists", AttributeOperator.NotExists)]
    public void Operator_aliases_are_accepted(string raw, AttributeOperator expected)
    {
        var predicates = AttributePredicateJsonReader.Parse(
            $"[{{\"attribute\":\"x\",\"op\":\"{raw}\",\"value\":\"1\"}}]");

        Assert.Equal(expected, predicates[0].Op);
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            AttributePredicateJsonReader.Parse("[{\"attribute\":\"x\",\"op\":\"between\",\"value\":\"1\"}]"));
        Assert.Contains("between", ex.Message);
    }

    [Fact]
    public void Comparison_operator_without_value_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AttributePredicateJsonReader.Parse("[{\"attribute\":\"x\",\"op\":\"ge\"}]"));
    }

    [Fact]
    public void Malformed_json_throws()
    {
        Assert.Throws<ArgumentException>(() => AttributePredicateJsonReader.Parse("not json"));
    }

    [Fact]
    public void Empty_predicate_set_matches_everything()
    {
        var feature = FeatureWith(("objectName", "Foo"));
        Assert.True(AttributePredicateEvaluator.Matches(feature, ImmutableArray<AttributePredicate>.Empty));
        Assert.True(AttributePredicateEvaluator.Matches(feature, default));
    }

    [Fact]
    public void Eq_is_case_insensitive_on_key_and_value()
    {
        var feature = FeatureWith(("ObjectName", "Foo"));
        var predicates = ImmutableArray.Create(new AttributePredicate("objectname", AttributeOperator.Eq, "foo"));
        Assert.True(AttributePredicateEvaluator.Matches(feature, predicates));
    }

    [Fact]
    public void Ne_matches_when_absent_or_different()
    {
        var feature = FeatureWith(("a", "1"));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("missing", AttributeOperator.Ne, "1"))));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("a", AttributeOperator.Ne, "2"))));
        Assert.False(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("a", AttributeOperator.Ne, "1"))));
    }

    [Fact]
    public void Exists_and_not_exists()
    {
        var feature = FeatureWith(("a", "1"));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("a", AttributeOperator.Exists, null))));
        Assert.False(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("b", AttributeOperator.Exists, null))));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("b", AttributeOperator.NotExists, null))));
    }

    [Fact]
    public void Contains_and_starts_with()
    {
        var feature = FeatureWith(("objectName", "North Channel Buoy"));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("objectName", AttributeOperator.Contains, "channel"))));
        Assert.True(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("objectName", AttributeOperator.StartsWith, "north"))));
        Assert.False(AttributePredicateEvaluator.Matches(feature,
            ImmutableArray.Create(new AttributePredicate("objectName", AttributeOperator.StartsWith, "south"))));
    }

    [Theory]
    [InlineData(AttributeOperator.Gt, "10", true)]
    [InlineData(AttributeOperator.Gt, "12.5", false)]
    [InlineData(AttributeOperator.Ge, "12.5", true)]
    [InlineData(AttributeOperator.Lt, "20", true)]
    [InlineData(AttributeOperator.Le, "12.5", true)]
    [InlineData(AttributeOperator.Le, "1", false)]
    public void Numeric_operators_compare_invariant(AttributeOperator op, string operand, bool expected)
    {
        var feature = FeatureWith(("valueOfDepth", "12.5"));
        var predicates = ImmutableArray.Create(new AttributePredicate("valueOfDepth", op, operand));
        Assert.Equal(expected, AttributePredicateEvaluator.Matches(feature, predicates));
    }

    [Fact]
    public void Numeric_operator_on_non_numeric_value_does_not_match()
    {
        var feature = FeatureWith(("objectName", "Foo"));
        var predicates = ImmutableArray.Create(new AttributePredicate("objectName", AttributeOperator.Gt, "1"));
        Assert.False(AttributePredicateEvaluator.Matches(feature, predicates));
    }

    [Fact]
    public void Multiple_predicates_are_anded()
    {
        var feature = FeatureWith(("valueOfDepth", "12.5"), ("objectName", "Foo"));
        var pass = ImmutableArray.Create(
            new AttributePredicate("valueOfDepth", AttributeOperator.Ge, "10"),
            new AttributePredicate("objectName", AttributeOperator.Eq, "Foo"));
        var fail = ImmutableArray.Create(
            new AttributePredicate("valueOfDepth", AttributeOperator.Ge, "10"),
            new AttributePredicate("objectName", AttributeOperator.Eq, "Bar"));
        Assert.True(AttributePredicateEvaluator.Matches(feature, pass));
        Assert.False(AttributePredicateEvaluator.Matches(feature, fail));
    }
}
