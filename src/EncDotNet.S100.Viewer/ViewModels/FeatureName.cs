using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Derives a single mariner-facing "human name" for a picked feature from
/// its decoded attributes. S-100 products carry an instance name in a small
/// set of well-known attributes (e.g. S-101 <c>featureName/name</c>, the
/// simple <c>name</c>, S-57 <c>OBJNAM</c>); this helper looks for the first
/// such value so the Pick Report can lead with "Number 10" rather than the
/// feature class. Returns <see langword="null"/> when no name-bearing
/// attribute is present, in which case callers fall back to the feature
/// class name.
/// </summary>
internal static class FeatureName
{
    // Codes that carry an instance name, in preference order. Compared
    // case-insensitively against PickAttribute.Code. "featureName" is the
    // S-100 complex attribute whose value lives in a child "name"; the rest
    // are simple string attributes used across products / S-57.
    private static readonly string[] NameCodes =
    [
        "featureName",
        "name",
        "objectName",
        "stationName",
        "OBJNAM",
    ];

    /// <summary>
    /// Returns the best human name found in <paramref name="attributes"/>,
    /// or <see langword="null"/> when none of the recognised name-bearing
    /// attributes are present or all are blank.
    /// </summary>
    /// <param name="attributes">The feature's decoded attribute rows.</param>
    public static string? Derive(IReadOnlyList<PickAttribute>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return null;

        foreach (var code in NameCodes)
        {
            foreach (var attr in attributes)
            {
                if (!string.Equals(attr.Code, code, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = ValueOf(attr);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the display value of a (possibly complex) name attribute.
    /// Complex attributes such as S-101 <c>featureName</c> hold their text
    /// in a child <c>name</c> row, so prefer that child when the parent has
    /// no direct value.
    /// </summary>
    private static string? ValueOf(PickAttribute attr)
    {
        var direct = attr.DisplayText;
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        foreach (var child in attr.Children)
        {
            if (string.Equals(child.Code, "name", StringComparison.OrdinalIgnoreCase))
            {
                var childValue = child.DisplayText;
                if (!string.IsNullOrWhiteSpace(childValue))
                    return childValue;
            }
        }

        return null;
    }
}
