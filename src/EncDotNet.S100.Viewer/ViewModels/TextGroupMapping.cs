using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The three quick text-group toggles exposed on the toolbar pill.
/// </summary>
internal enum TextGroup
{
    /// <summary>Important navigational text (e.g. S-101 VG layer "Important Text").</summary>
    Important,

    /// <summary>Non-critical text (e.g. S-101 VG layer "Other Text").</summary>
    Other,

    /// <summary>Remaining chart text not covered by the previous two groups.</summary>
    All,
}

/// <summary>
/// Resolves the three toolbar-level text groups to the concrete
/// viewing-group ids declared by a spec's portrayal catalogue.
/// The mapping uses the <see cref="ViewingGroupLayer.Description"/>
/// name rather than hard-coding layer ids so future PC updates
/// don't break the mapping.
/// </summary>
internal static class TextGroupMapping
{
    /// <summary>Canonical name strings used in the S-101 PC.</summary>
    private static readonly IReadOnlyDictionary<TextGroup, string> CanonicalNames =
        new Dictionary<TextGroup, string>
        {
            [TextGroup.Important] = "Important Text",
            [TextGroup.Other] = "Other Text",
            [TextGroup.All] = "All other chart text",
        };

    /// <summary>
    /// Returns the viewing-group ids that belong to
    /// <paramref name="group"/> according to the given
    /// <paramref name="catalogue"/>. Returns an empty set when the
    /// catalogue does not declare a matching layer (non-S-101 specs).
    /// </summary>
    public static IReadOnlySet<int> Resolve(TextGroup group, PortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (!CanonicalNames.TryGetValue(group, out var name))
            return new HashSet<int>();

        var layer = catalogue.ViewingGroupLayers
            .FirstOrDefault(l => l.Description.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (layer is null)
            return new HashSet<int>();

        var ids = new HashSet<int>();
        foreach (var vgId in layer.ViewingGroupIds)
        {
            if (int.TryParse(vgId, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>
    /// Returns <c>true</c> when the catalogue has at least one text
    /// viewing-group layer (i.e. the toolbar pill should be active
    /// for this spec).
    /// </summary>
    public static bool HasTextLayers(PortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return CanonicalNames.Values.Any(name =>
            catalogue.ViewingGroupLayers.Any(l =>
                l.Description.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }
}
