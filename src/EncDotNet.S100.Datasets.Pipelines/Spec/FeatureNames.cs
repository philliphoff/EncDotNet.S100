using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Extracts human-readable name candidates from an
/// <see cref="IS100Feature"/> so that name-oriented tools (e.g.
/// <see cref="SearchFeaturesTool"/>) can search across the several places
/// a feature's name may live, independent of encoding.
/// </summary>
/// <remarks>
/// <para>
/// Names are not represented uniformly across the S-100 product line:
/// </para>
/// <list type="bullet">
/// <item><description>
/// GML-encoded specs typically carry a repeatable complex attribute
/// <c>featureName</c> whose <c>name</c> / <c>displayName</c>
/// sub-attributes hold the text (S-100 Part 5 generic
/// <c>featureName</c> compound).
/// </description></item>
/// <item><description>
/// Some specs also expose a simple <c>objectName</c> attribute, and the
/// ISO 8211-encoded S-101 surfaces the legacy <c>OBJNAM</c> / <c>NOBJNM</c>
/// simple attributes through its pipeline feature shape.
/// </description></item>
/// </list>
/// <para>
/// This helper unifies all of those into a flat list of
/// (source, value) pairs so a caller can match a query against any of
/// them without knowing the encoding.
/// </para>
/// </remarks>
public static class FeatureNames
{
    private static readonly HashSet<string> SimpleNameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "objectName",
        "featureName",
        "name",
        "displayName",
        "OBJNAM",
        "NOBJNM",
        "nationalName",
    };

    private static readonly HashSet<string> ComplexNameCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "featureName",
        "objectName",
    };

    private static readonly HashSet<string> ComplexNameSubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "displayName",
    };

    /// <summary>
    /// Enumerates the non-empty name candidates for <paramref name="feature"/>,
    /// each paired with the attribute path it came from (e.g. <c>OBJNAM</c>
    /// or <c>featureName.name</c>). Duplicate values from the same source
    /// path are not deduplicated.
    /// </summary>
    public static IEnumerable<(string Source, string Value)> Enumerate(IS100Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        foreach (var (key, value) in feature.Attributes)
        {
            if (!string.IsNullOrWhiteSpace(value) && SimpleNameKeys.Contains(key))
            {
                yield return (key, value);
            }
        }

        foreach (var complex in feature.ComplexAttributes)
        {
            if (!ComplexNameCodes.Contains(complex.Code))
            {
                continue;
            }

            foreach (var (subKey, subValue) in complex.SubAttributes)
            {
                if (!string.IsNullOrWhiteSpace(subValue) && ComplexNameSubKeys.Contains(subKey))
                {
                    yield return ($"{complex.Code}.{subKey}", subValue);
                }
            }
        }
    }
}
