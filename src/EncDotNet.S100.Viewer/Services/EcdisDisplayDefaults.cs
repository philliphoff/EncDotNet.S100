using System.Globalization;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Seeds the viewer's default ECDIS viewing-group visibility.
/// </summary>
/// <remarks>
/// <para>
/// The S-101 portrayal catalogue declares a handful of "Independent
/// Mariner Selector" viewing groups (S-101 PC §Independent Mariner
/// Selectors) that operate independently of the selected display mode.
/// They are not part of any <c>displayMode</c> membership, so they are
/// already off in Display Base / Standard / Other Information, but they
/// switch <em>on</em> in the "All" category (where there is no mode
/// filter, so every viewing group is base-on).
/// </para>
/// <para>
/// Three of them paint a repeating area-fill pattern across the whole
/// dataset, which makes the chart busy and is expensive to rasterise
/// during pan/zoom:
/// </para>
/// <list type="bullet">
///   <item><c>90000</c> — Shallow water pattern (DIAMON1 diamond fill on
///   areas shallower than the safety contour).</item>
///   <item><c>90010</c> — Survey accuracy / reliability / quality
///   (M_ACCY / M_SREL / M_QUAL CATZOC star patterns).</item>
///   <item><c>90011</c> — Low-accuracy data marker (LOWACC01).</item>
/// </list>
/// <para>
/// The viewer therefore hides these by default — including in "All" —
/// while still surfacing them in the ECDIS panel so the mariner can
/// switch them back on. This is applied once per profile via the
/// <see cref="ViewerSettings.EcdisDefaultsApplied"/> flag so that a
/// mariner who later re-enables them is never overridden on the next
/// launch.
/// </para>
/// </remarks>
internal static class EcdisDisplayDefaults
{
    /// <summary>
    /// Per-spec viewing-group ids hidden by default (including in the
    /// "All" category). Keys are spec codes (e.g. <c>"S-101"</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> DefaultHiddenViewingGroups =
        new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-101"] = new HashSet<int> { 90000, 90010, 90011 },
        };

    /// <summary>
    /// Merges <see cref="DefaultHiddenViewingGroups"/> into
    /// <paramref name="settings"/> exactly once (guarded by
    /// <see cref="ViewerSettings.EcdisDefaultsApplied"/>). Existing
    /// hidden ids are preserved; the flag is set so subsequent launches
    /// respect whatever the mariner has since configured.
    /// </summary>
    /// <param name="settings">The settings to mutate in place.</param>
    /// <returns>
    /// <c>true</c> when the defaults were applied (the caller should
    /// persist <paramref name="settings"/>); <c>false</c> when they had
    /// already been applied and nothing changed.
    /// </returns>
    public static bool Apply(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.EcdisDefaultsApplied)
            return false;

        foreach (var (spec, ids) in DefaultHiddenViewingGroups)
        {
            settings.EcdisHiddenViewingGroups.TryGetValue(spec, out var existingCsv);
            var merged = ParseIds(existingCsv);
            foreach (var id in ids)
                merged.Add(id);

            settings.EcdisHiddenViewingGroups[spec] =
                string.Join(",", merged.OrderBy(i => i));
        }

        settings.EcdisDefaultsApplied = true;
        return true;
    }

    private static SortedSet<int> ParseIds(string? csv)
    {
        var ids = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(csv))
            return ids;

        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                ids.Add(id);
        }

        return ids;
    }
}
