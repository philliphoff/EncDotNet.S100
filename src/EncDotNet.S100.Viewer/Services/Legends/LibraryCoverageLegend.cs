using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The Library coverage section of the Legend tab (legends design note D5,
/// D8): while the Library panel's outlines are on the map, one row per
/// outline style drawn, styled from <see cref="LibraryOutlineStyles"/> like the
/// Library rows' swatches. The swatches stay in the panel; this mirrors them.
/// </summary>
internal static class LibraryCoverageLegend
{
    /// <summary>The section's id (<c>Legend.Section.Library</c>).</summary>
    public const string SectionId = "Library";

    /// <summary>
    /// The section for the outline styles drawn now, in the Library's order
    /// (local, online, listed, missing, update, expired), then the selected
    /// dataset's accent outline; null when nothing is outlined.
    /// </summary>
    /// <param name="drawn">The primary availabilities of the outlines drawn.</param>
    /// <param name="outlined">How many datasets and areas are outlined.</param>
    /// <param name="selectedAccent">The accent the selected dataset is outlined in, or null when none is.</param>
    public static LegendSection? Build(
        IReadOnlyCollection<LibraryPrimaryAvailability> drawn, int outlined, (byte R, byte G, byte B)? selectedAccent)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        if (outlined <= 0)
            return null;

        var rows = new List<LegendEntry>();
        foreach (var state in Enum.GetValues<LibraryPrimaryAvailability>())
        {
            if (drawn.Contains(state))
                rows.Add(new LineLegendEntry(Sample(LibraryOutlineStyles.For(state)), Label(state)));
        }

        if (selectedAccent is { } accent)
        {
            rows.Add(new LineLegendEntry(
                LegendLineSample.FromPixels(new RgbaColor(accent.R, accent.G, accent.B), LibraryOutlineStyles.SelectedWidth, []),
                Strings.Legend_LibrarySelected));
        }

        return new LegendSection
        {
            Id = SectionId,
            Spec = Strings.Legend_LibrarySpec,
            Title = Strings.Legend_LibraryTitle,
            Source = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                outlined == 1 ? Strings.Legend_LibraryOutlinedOneFormat : Strings.Legend_LibraryOutlinedFormat,
                outlined),
            DatasetIds = [],
            IsInView = true,
            IsExpanded = true,
            Plane = S98DisplayPlane.MarinerOverlay,
            Groups = [new LegendGroup("library.coverage", Strings.Legend_LibraryTitle, rows)],
        };
    }

    /// <summary>The row label: the Library's own word for the state.</summary>
    /// <param name="state">The primary availability.</param>
    public static string Label(LibraryPrimaryAvailability state) => state switch
    {
        LibraryPrimaryAvailability.Local => Strings.Library_Availability_Local,
        LibraryPrimaryAvailability.Online => Strings.Library_Availability_Online,
        LibraryPrimaryAvailability.Missing => Strings.Library_Availability_Missing,
        LibraryPrimaryAvailability.Update => Strings.Library_Availability_Outdated,
        LibraryPrimaryAvailability.Expired => Strings.Library_Tag_Expired,
        _ => Strings.Library_Availability_Listed,
    };

    /// <summary>
    /// The outline style as a legend line sample. A dotted style (a
    /// zero-length dash with round caps) becomes dots one line width long,
    /// since the legend's pen has butt caps.
    /// </summary>
    /// <param name="style">The outline style.</param>
    internal static LegendLineSample Sample(LibraryOutlineStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        IReadOnlyList<double> dashes = style.DashArray is { Count: > 0 } d
            ? [.. d.Select((x, i) => i % 2 == 0 && style.RoundCap ? Math.Max(x, style.Width)
                : i % 2 == 1 && style.RoundCap ? Math.Max(x - style.Width, 1) : x)]
            : [];
        return LegendLineSample.FromPixels(
            new RgbaColor(style.Color.R, style.Color.G, style.Color.B), style.Width, dashes);
    }
}
