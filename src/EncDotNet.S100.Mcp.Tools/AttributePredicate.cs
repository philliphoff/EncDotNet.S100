using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Comparison operators supported by an <see cref="AttributePredicate"/>.
/// </summary>
public enum AttributeOperator
{
    /// <summary>The attribute is present (any value).</summary>
    Exists,

    /// <summary>The attribute is absent.</summary>
    NotExists,

    /// <summary>The attribute is present and equals the value (case-insensitive).</summary>
    Eq,

    /// <summary>The attribute is absent, or present with a different value (case-insensitive).</summary>
    Ne,

    /// <summary>The attribute is present and contains the value as a substring (case-insensitive).</summary>
    Contains,

    /// <summary>The attribute is present and starts with the value (case-insensitive).</summary>
    StartsWith,

    /// <summary>The attribute is present, numeric, and greater than the value.</summary>
    Gt,

    /// <summary>The attribute is present, numeric, and greater than or equal to the value.</summary>
    Ge,

    /// <summary>The attribute is present, numeric, and less than the value.</summary>
    Lt,

    /// <summary>The attribute is present, numeric, and less than or equal to the value.</summary>
    Le,
}

/// <summary>
/// A single predicate over a feature's simple attributes, used by
/// <see cref="QueryFeaturesTool"/> to filter on attribute values.
/// </summary>
/// <param name="Attribute">The attribute code to test (matched case-insensitively against the feature's attribute keys).</param>
/// <param name="Op">The comparison operator.</param>
/// <param name="Value">The comparison operand; ignored for <see cref="AttributeOperator.Exists"/> / <see cref="AttributeOperator.NotExists"/>.</param>
public sealed record AttributePredicate(
    [property: Description("The attribute code to test (matched case-insensitively against the feature's attribute keys).")] string Attribute,
    [property: Description("The comparison operator.")] AttributeOperator Op,
    [property: Description("The comparison operand; ignored for exists / notExists.")] string? Value);

/// <summary>
/// Parses the JSON envelope accepted by the <c>attributes</c> parameter
/// of <see cref="QueryFeaturesTool"/> into a set of
/// <see cref="AttributePredicate"/>s.
/// </summary>
/// <remarks>
/// Two shapes are accepted:
/// <list type="bullet">
/// <item><description>
/// A compact object map, e.g. <c>{"categoryOfLateralMark":"1","objectName":"Foo"}</c>,
/// where each entry becomes an <see cref="AttributeOperator.Eq"/> predicate.
/// </description></item>
/// <item><description>
/// An explicit array, e.g.
/// <c>[{"attribute":"valueOfDepth","op":"ge","value":"10"},{"attribute":"objectName","op":"exists"}]</c>.
/// </description></item>
/// </list>
/// Multiple predicates are combined with logical AND.
/// </remarks>
public static class AttributePredicateJsonReader
{
    /// <summary>Parses <paramref name="json"/> into predicates.</summary>
    /// <exception cref="ArgumentException">The JSON is malformed or names an unknown operator.</exception>
    public static ImmutableArray<AttributePredicate> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"attributes is not valid JSON: {ex.Message}", nameof(json));
        }

        using (doc)
        {
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Object => ParseObject(root),
                JsonValueKind.Array => ParseArray(root),
                _ => throw new ArgumentException(
                    "attributes must be a JSON object (code→value map) or an array of predicate objects.",
                    nameof(json)),
            };
        }
    }

    private static ImmutableArray<AttributePredicate> ParseObject(JsonElement obj)
    {
        var builder = ImmutableArray.CreateBuilder<AttributePredicate>();
        foreach (var property in obj.EnumerateObject())
        {
            builder.Add(new AttributePredicate(property.Name, AttributeOperator.Eq, ReadScalar(property.Value)));
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<AttributePredicate> ParseArray(JsonElement array)
    {
        var builder = ImmutableArray.CreateBuilder<AttributePredicate>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("each attributes entry must be an object with 'attribute' and 'op'.", nameof(array));
            }

            if (!item.TryGetProperty("attribute", out var attrEl) || attrEl.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("each attributes entry requires a string 'attribute'.", nameof(array));
            }

            var op = item.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String
                ? ParseOperator(opEl.GetString()!)
                : AttributeOperator.Eq;

            string? value = item.TryGetProperty("value", out var valueEl) ? ReadScalar(valueEl) : null;

            if (op is not (AttributeOperator.Exists or AttributeOperator.NotExists) && value is null)
            {
                throw new ArgumentException(
                    $"attribute predicate on '{attrEl.GetString()}' with operator '{op}' requires a 'value'.",
                    nameof(array));
            }

            builder.Add(new AttributePredicate(attrEl.GetString()!, op, value));
        }
        return builder.ToImmutable();
    }

    private static string? ReadScalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => throw new ArgumentException("attribute values must be string, number, or boolean."),
    };

    private static AttributeOperator ParseOperator(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "exists" => AttributeOperator.Exists,
        "notexists" or "not_exists" or "absent" => AttributeOperator.NotExists,
        "eq" or "=" or "==" => AttributeOperator.Eq,
        "ne" or "!=" or "<>" => AttributeOperator.Ne,
        "contains" => AttributeOperator.Contains,
        "startswith" or "starts_with" or "prefix" => AttributeOperator.StartsWith,
        "gt" or ">" => AttributeOperator.Gt,
        "ge" or ">=" => AttributeOperator.Ge,
        "lt" or "<" => AttributeOperator.Lt,
        "le" or "<=" => AttributeOperator.Le,
        _ => throw new ArgumentException($"unknown attribute operator '{raw}'."),
    };
}

/// <summary>
/// Evaluates <see cref="AttributePredicate"/>s against a feature's simple
/// attributes. All predicates must hold (logical AND).
/// </summary>
public static class AttributePredicateEvaluator
{
    /// <summary>
    /// Returns <c>true</c> when every predicate in
    /// <paramref name="predicates"/> holds for <paramref name="feature"/>.
    /// An empty predicate set matches every feature.
    /// </summary>
    public static bool Matches(IS100Feature feature, ImmutableArray<AttributePredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (predicates.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var predicate in predicates)
        {
            if (!Evaluate(feature, predicate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Evaluate(IS100Feature feature, AttributePredicate predicate)
    {
        var present = TryGet(feature.Attributes, predicate.Attribute, out var actual);

        switch (predicate.Op)
        {
            case AttributeOperator.Exists:
                return present;
            case AttributeOperator.NotExists:
                return !present;
            case AttributeOperator.Ne:
                return !present || !string.Equals(actual, predicate.Value, StringComparison.OrdinalIgnoreCase);
        }

        if (!present || actual is null)
        {
            return false;
        }

        return predicate.Op switch
        {
            AttributeOperator.Eq => string.Equals(actual, predicate.Value, StringComparison.OrdinalIgnoreCase),
            AttributeOperator.Contains => actual.Contains(predicate.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            AttributeOperator.StartsWith => actual.StartsWith(predicate.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            AttributeOperator.Gt => CompareNumeric(actual, predicate.Value) is { } c && c > 0,
            AttributeOperator.Ge => CompareNumeric(actual, predicate.Value) is { } c && c >= 0,
            AttributeOperator.Lt => CompareNumeric(actual, predicate.Value) is { } c && c < 0,
            AttributeOperator.Le => CompareNumeric(actual, predicate.Value) is { } c && c <= 0,
            _ => false,
        };
    }

    private static bool TryGet(ImmutableDictionary<string, string> attributes, string key, out string? value)
    {
        if (attributes.TryGetValue(key, out var exact))
        {
            value = exact;
            return true;
        }

        foreach (var kvp in attributes)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static int? CompareNumeric(string actual, string? operand)
    {
        if (operand is null
            || !double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            || !double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            return null;
        }

        return a.CompareTo(b);
    }
}
