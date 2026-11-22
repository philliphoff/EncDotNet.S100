using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Seeds the viewer's default ECDIS viewing-group visibility from
/// <see cref="EcdisDisplaySettings.DefaultHiddenViewingGroups"/>.
/// </summary>
/// <remarks>
/// The default set (the S-101 shallow-water pattern, data-quality patterns
/// and low-accuracy marker) is shared with the <c>s100 tiles</c> commands, so
/// both hosts hide the same groups. The viewer still lists them in the ECDIS
/// panel so the mariner can switch them back on. They are merged in once per
/// profile via the <see cref="ViewerSettings.EcdisDefaultsApplied"/> flag, so
/// a mariner who later re-enables them is never overridden on the next launch.
/// </remarks>
internal static class EcdisDisplayDefaults
{
    /// <summary>
    /// Merges <see cref="EcdisDisplaySettings.DefaultHiddenViewingGroups"/> into
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

        foreach (var (spec, ids) in EcdisDisplaySettings.DefaultHiddenViewingGroups)
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
