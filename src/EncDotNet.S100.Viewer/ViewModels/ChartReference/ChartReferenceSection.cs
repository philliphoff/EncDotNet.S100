using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A Chart 1 section, as a viewing group's description names it, such as
/// <c>Standard: P, Q, R, S - Buoys and Beacons, Lights, Fog signals, RADAR</c>
/// (symbol dictionary design note §4.5).
/// </summary>
/// <param name="Key">
/// The section's letters, such as <c>P, Q, R, S</c>, which the list groups
/// by; descriptions in different display categories or spellings share it.
/// </param>
/// <param name="Title">The section's letters and name, such as <c>P, Q, R, S – Buoys and Beacons, …</c>.</param>
internal sealed partial record ChartReferenceSection(string Key, string Title)
{
    /// <summary>
    /// The section a viewing group names, or <see langword="null"/> when its
    /// description doesn't start with Chart 1 letters.
    /// </summary>
    /// <param name="group">The viewing group.</param>
    /// <returns>The section, or <see langword="null"/>.</returns>
    public static ChartReferenceSection? Of(ViewingGroupInfo? group)
    {
        if (group?.Description is not { Length: > 0 } description)
            return null;
        var match = SectionPattern().Match(description.Trim());
        if (!match.Success)
            return null;
        var letters = string.Join(", ", match.Groups["letters"].Value.Split(',', StringSplitOptions.TrimEntries));
        var name = match.Groups["name"].Value.Trim();
        return new ChartReferenceSection(letters, name.Length > 0 ? $"{letters} – {name}" : letters);
    }

    // An optional display category ("Base:", "Standard:", "Other:"), then
    // capital letters separated by commas, then an optional "-" and a name.
    [GeneratedRegex(@"^(?:[A-Za-z ]+:\s*)?(?<letters>[A-Z](?:\s*,\s*[A-Z])*)(?![A-Za-z])\s*-?\s*(?<name>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();
}
