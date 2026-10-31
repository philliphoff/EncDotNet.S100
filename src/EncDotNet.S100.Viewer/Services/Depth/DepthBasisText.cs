using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>How the depth basis pill is emphasised.</summary>
internal enum DepthBasisTone
{
    /// <summary>Neutral, like the scale readout: plain charted depths.</summary>
    Neutral,

    /// <summary>Informational accent: S-104 in view but not applied.</summary>
    Info,
}

/// <summary>
/// The pill's three whole forms (<c>docs/design/depth-basis-indicator.md</c>
/// §2.2.1): the view draws the longest that fits and never truncates.
/// </summary>
/// <param name="Full">The full value; also the tooltip, accessible name and flyout's first line.</param>
/// <param name="Short">The value with its qualifier shortened.</param>
/// <param name="Compact">The shortest value; shown with the state's icon.</param>
/// <param name="Tone">The emphasis.</param>
/// <param name="ShowsIcon">True when the state has an icon (always drawn with <paramref name="Compact"/>).</param>
internal sealed record DepthBasisForms(string Full, string Short, string Compact, DepthBasisTone Tone, bool ShowsIcon);

/// <summary>One line of the flyout's datum section.</summary>
/// <param name="Label">The datum's name, or "Not declared".</param>
/// <param name="Datasets">The datasets on it, when several datums are in view; otherwise empty.</param>
internal sealed record DepthBasisDatumLine(string Label, string Datasets);

/// <summary>
/// Turns a <see cref="DepthBasis"/> into the words the status-bar pill, its
/// flyout, the depth card and <c>get_depth_basis</c> show. The only place
/// those strings are chosen.
/// </summary>
/// <remarks>
/// The adjusted states (<see cref="DepthBasisKind.Adjusted"/> and
/// <see cref="DepthBasisReason.WaterLevel"/>) get their strings with chart
/// adjustment (#1132). Until then <see cref="NoWaterLevelAdjustment"/> never
/// reports them, and asking for their text throws.
/// </remarks>
internal static class DepthBasisText
{
    /// <summary>The most sources the flyout lists before "and N more".</summary>
    internal const int MaxListedSources = 5;

    /// <summary>The pill's forms for a basis.</summary>
    /// <param name="basis">The basis; not <see cref="DepthBasisKind.None"/>.</param>
    /// <returns>The forms.</returns>
    public static DepthBasisForms Pill(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return (basis.Kind, basis.Reason) switch
        {
            (DepthBasisKind.None, _) => new(string.Empty, string.Empty, string.Empty, DepthBasisTone.Neutral, false),
            (DepthBasisKind.Charted, DepthBasisReason.None) => new(
                Strings.Status_DepthBasis_Charted,
                Strings.Status_DepthBasis_Charted,
                Strings.Status_DepthBasis_Charted,
                DepthBasisTone.Neutral,
                false),
            (DepthBasisKind.Charted, DepthBasisReason.NotApplied) => new(
                Strings.Status_DepthBasis_ChartedNotApplied,
                Strings.Status_DepthBasis_ChartedNotApplied_Short,
                Strings.Status_DepthBasis_ChartedNotApplied_Compact,
                DepthBasisTone.Info,
                true),
            _ => throw new NotSupportedException(
                $"The depth basis {basis.Kind}/{basis.Reason} has no text until chart water level adjustment (#1132) adds it."),
        };
    }

    /// <summary>The pill text exactly as drawn at its widest (<c>get_depth_basis</c>'s <c>display</c>).</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The full form; empty when hidden.</returns>
    public static string Display(DepthBasis basis) => Pill(basis).Full;

    /// <summary>
    /// The pill's accessible name, with the state, e.g. "Depth basis: Charted,
    /// water level not applied", so a screen reader announces it without
    /// opening the flyout.
    /// </summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The name.</returns>
    public static string AccessibleName(DepthBasis basis)
    {
        var full = Display(basis);
        return full.Length == 0
            ? Strings.Label_DepthBasis
            : string.Format(CultureInfo.CurrentCulture, Strings.Label_DepthBasisStateFormat, full.Replace(" · ", ", ", StringComparison.Ordinal));
    }

    /// <summary>The flyout's line on what the chart's depths are.</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The line.</returns>
    public static string BasisLine(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return basis.Kind == DepthBasisKind.Adjusted
            ? throw new NotSupportedException("Adjusted depths get their text with #1132.")
            : Strings.Status_DepthBasisPopup_ChartedBasis;
    }

    /// <summary>The depth card's line on how the chart relates to the picked depth.</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The line; empty when hidden.</returns>
    public static string CardLine(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return basis.Kind switch
        {
            DepthBasisKind.None => string.Empty,
            DepthBasisKind.Charted => Strings.Pick_Depth_ChartBasisCharted,
            _ => throw new NotSupportedException("Adjusted depths get their text with #1132."),
        };
    }

    /// <summary>
    /// The datum section: one line with the datum's name when every depth
    /// source in view shares it; otherwise one line per datum with the
    /// datasets on it, after <see cref="DatumHeading"/>.
    /// </summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The lines; empty when no depth source is in view.</returns>
    public static IReadOnlyList<DepthBasisDatumLine> DatumLines(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var groups = basis.DepthSources
            .GroupBy(s => s.VerticalDatumCode)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key)
            .ToList();
        if (groups.Count == 1)
            return [new DepthBasisDatumLine(DatumLabel(groups[0].Key), string.Empty)];

        return [.. groups.Select(g => new DepthBasisDatumLine(
            DatumLabel(g.Key),
            string.Join(", ", g.Select(s => s.DatasetId))))];
    }

    /// <summary>"Several datums in view" when the depth sources disagree, otherwise <see langword="null"/>.</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The heading, or <see langword="null"/>.</returns>
    public static string? DatumHeading(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return basis.DepthSources.Select(s => s.VerticalDatumCode).Distinct().Count() > 1
            ? Strings.Status_DepthBasisPopup_SeveralDatums
            : null;
    }

    /// <summary>A datum's name: the S-100 label, the code when unlisted, or "Not declared".</summary>
    /// <param name="code">The S-100 vertical datum code.</param>
    /// <returns>The name.</returns>
    public static string DatumLabel(int? code) => code switch
    {
        null => Strings.Status_DepthBasisPopup_DatumNotDeclared,
        { } c when VerticalDatums.TryGetLabel(c, out var label) => label,
        { } c => c.ToString(CultureInfo.CurrentCulture),
    };

    /// <summary>The depth sources in view, capped at <see cref="MaxListedSources"/> with "and N more".</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The lines; "None in view" when none.</returns>
    public static IReadOnlyList<string> SourceLines(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        if (basis.DepthSources.Count == 0)
            return [Strings.Status_DepthBasisPopup_NoSourcesInView];

        var lines = basis.DepthSources.Take(MaxListedSources).Select(s => s.DatasetId).ToList();
        if (basis.DepthSources.Count > MaxListedSources)
        {
            lines.Add(string.Format(
                CultureInfo.CurrentCulture, Strings.Status_DepthBasisPopup_MoreFormat, basis.DepthSources.Count - MaxListedSources));
        }

        return lines;
    }

    /// <summary>
    /// The water level section: "No S-104 water level loaded.", or each S-104
    /// dataset meeting the view (its kind, run and time range) and "Not
    /// applied to the chart."
    /// </summary>
    /// <param name="basis">The basis.</param>
    /// <param name="waterLevelLoaded">True when an active S-104 dataset is loaded, in view or not.</param>
    /// <param name="format">The user's Local/UTC setting.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> WaterLevelLines(DepthBasis basis, bool waterLevelLoaded, TimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(basis);
        if (basis.WaterLevelSources.Count == 0)
        {
            return [waterLevelLoaded
                ? Strings.Status_DepthBasisPopup_NoWaterLevelInView
                : Strings.Status_DepthBasisPopup_NoWaterLevel];
        }

        var lines = new List<string>();
        foreach (var source in basis.WaterLevelSources)
        {
            var parts = new List<string>();
            if (source.Kind is { } kind)
            {
                parts.Add(kind == WaterLevelSourceKind.Station
                    ? Strings.Status_DepthBasisPopup_WaterLevelStation
                    : Strings.Status_DepthBasisPopup_WaterLevelGrid);
            }
            if (source.Run is { } run)
            {
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture, Strings.Status_DepthBasisPopup_RunFormat, TimeFormatting.Format(run, format)));
            }
            if (source.Start is { } start && source.End is { } end)
                parts.Add(TimeFormatting.FormatTimeRange(start, end, format));

            lines.Add(parts.Count == 0
                ? source.DatasetId
                : string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Status_DepthBasisPopup_WaterLevelSourceFormat,
                    source.DatasetId,
                    string.Join(" · ", parts)));
        }

        if (basis.Kind == DepthBasisKind.Charted && basis.Reason == DepthBasisReason.NotApplied)
            lines.Add(Strings.Status_DepthBasisPopup_NotApplied);

        return lines;
    }

    /// <summary>Where the viewer applies water level today.</summary>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> WhereAppliedLines() =>
    [
        Strings.Status_DepthBasisPopup_WherePick,
        Strings.Status_DepthBasisPopup_WhereRoute,
    ];
}
