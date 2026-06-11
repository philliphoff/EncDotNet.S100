using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Attribute-matching helpers for <see cref="IGmlFeature"/> instances,
/// powering the optional attribute filter shared by
/// <see cref="EncDotNet.S100.Mcp.Tools.QueryFeaturesTool"/> and
/// <see cref="EncDotNet.S100.Mcp.Tools.FindNearestTool"/>.
/// </summary>
/// <remarks>
/// Both a feature's simple <see cref="IGmlFeature.Attributes"/> and the
/// sub-attributes of its complex attributes
/// (<see cref="IGmlFeature.GmlComplexAttributes"/>) are searched, so a
/// caller need not know whether a given S-100 attribute is encoded
/// simple or nested. Codes are matched on the local name only — any
/// namespace prefix on the stored key is ignored.
/// </remarks>
public static class GmlFeatureAttributes
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="feature"/> satisfies
    /// every predicate in <paramref name="filter"/>. An empty (or
    /// default) filter always matches.
    /// </summary>
    public static bool Matches(IGmlFeature feature, AttributeFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (filter is null || filter.IsEmpty)
        {
            return true;
        }

        foreach (var predicate in filter.Predicates)
        {
            if (!MatchesPredicate(feature, predicate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesPredicate(IGmlFeature feature, AttributePredicate predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate.Code))
        {
            // A predicate with no code cannot constrain anything; treat
            // it as vacuously satisfied rather than silently dropping
            // every feature.
            return true;
        }

        var wantPresenceOnly = string.IsNullOrWhiteSpace(predicate.Value);
        var wanted = predicate.Value?.Trim();

        if (!feature.Attributes.IsEmpty)
        {
            foreach (var kvp in feature.Attributes)
            {
                if (!CodeMatches(kvp.Key, predicate.Code))
                {
                    continue;
                }
                if (wantPresenceOnly || ValueMatches(kvp.Value, wanted!))
                {
                    return true;
                }
            }
        }

        foreach (var complex in feature.GmlComplexAttributes)
        {
            if (complex.SubAttributes.IsEmpty)
            {
                continue;
            }
            foreach (var kvp in complex.SubAttributes)
            {
                if (!CodeMatches(kvp.Key, predicate.Code))
                {
                    continue;
                }
                if (wantPresenceOnly || ValueMatches(kvp.Value, wanted!))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CodeMatches(string storedKey, string wantedCode)
    {
        if (string.Equals(storedKey, wantedCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Tolerate namespace-prefixed or path-qualified keys by comparing
        // on the trailing local-name segment.
        var local = LocalName(storedKey);
        return string.Equals(local, wantedCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string LocalName(string key)
    {
        var sep = key.LastIndexOfAny(new[] { ':', '/', '.' });
        return sep >= 0 && sep < key.Length - 1 ? key[(sep + 1)..] : key;
    }

    private static bool ValueMatches(string storedValue, string wanted)
        => string.Equals(storedValue?.Trim(), wanted, StringComparison.OrdinalIgnoreCase);
}
