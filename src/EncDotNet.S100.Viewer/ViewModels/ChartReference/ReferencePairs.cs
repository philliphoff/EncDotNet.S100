using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One before/after pair of a display setting's entry (design note §4.4,
/// C4, R13c, #1722): what a feature type's rule draws on each side of the
/// setting, other inputs being equal.
/// </summary>
internal sealed class ReferencePair : ViewModelBase
{
    private string? _caption;

    /// <summary>Creates a pair.</summary>
    /// <param name="feature">The feature type's name.</param>
    /// <param name="before">The glyph with the setting off, or below its value.</param>
    /// <param name="after">The glyph with the setting on, or above its value.</param>
    /// <param name="caption">The values either side, for a number; <see langword="null"/> for an on/off setting.</param>
    public ReferencePair(string feature, ReferenceGlyphTile before, ReferenceGlyphTile after, string? caption = null)
    {
        Feature = feature;
        Before = before;
        After = after;
        _caption = caption;
    }

    /// <summary>The feature type's name.</summary>
    public string Feature { get; }

    /// <summary>The glyph with the setting off, or below its value.</summary>
    public ReferenceGlyphTile Before { get; }

    /// <summary>The glyph with the setting on, or above its value.</summary>
    public ReferenceGlyphTile After { get; }

    /// <summary>"valueOfSounding 29.9 → 30.1", or <see langword="null"/>.</summary>
    public string? Caption
    {
        get => _caption;
        set
        {
            if (SetProperty(ref _caption, value))
            {
                OnPropertyChanged(nameof(HasCaption));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    /// <summary>Whether there is a caption.</summary>
    public bool HasCaption => _caption is not null;

    /// <summary>"Lateral Beacon: BCNSTK02, then BCNLAT21".</summary>
    public string AccessibleName => _caption is null
        ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_PairName, Feature, Before.Label, After.Label)
        : string.Format(CultureInfo.CurrentCulture, Strings.Reference_PairNameCaption, Feature, Before.Label, After.Label, _caption);

    /// <summary>The values either side of a number, for <see cref="Caption"/>; the probe's difference from the value it was probed at.</summary>
    internal (string Attribute, double Below, double Above)? Offsets { get; init; }
}

/// <summary>
/// A feature type's before/after pairs in a display setting's entry,
/// capped at <see cref="Cap"/> with <b>Show all (n)</b> (R13c, #1722).
/// </summary>
internal sealed class ReferencePairGroup : ViewModelBase
{
    /// <summary>The most pairs a feature type shows before Show all.</summary>
    public const int Cap = 6;

    private readonly Action _shown;
    private IReadOnlyList<ReferencePair> _visible;

    /// <summary>Creates a group.</summary>
    /// <param name="feature">The link to the feature type.</param>
    /// <param name="pairs">Its pairs, in the order the probe found them.</param>
    /// <param name="shown">Called after Show all, so the glyphs it shows are drawn.</param>
    public ReferencePairGroup(ChartReferenceLinkViewModel feature, IReadOnlyList<ReferencePair> pairs, Action shown)
    {
        Feature = feature;
        Pairs = pairs;
        _shown = shown;
        _visible = pairs.Count > Cap ? [.. pairs.Take(Cap)] : pairs;
        ShowAllCommand = new RelayCommand(ShowAll);
    }

    /// <summary>The link to the feature type.</summary>
    public ChartReferenceLinkViewModel Feature { get; }

    /// <summary>Every pair.</summary>
    public IReadOnlyList<ReferencePair> Pairs { get; }

    /// <summary>The pairs shown: the first <see cref="Cap"/>, or every one after Show all.</summary>
    public IReadOnlyList<ReferencePair> Visible
    {
        get => _visible;
        private set
        {
            if (SetProperty(ref _visible, value))
                OnPropertyChanged(nameof(CanShowAll));
        }
    }

    /// <summary>Whether some pairs are hidden.</summary>
    public bool CanShowAll => _visible.Count < Pairs.Count;

    /// <summary>"Show all (35)".</summary>
    public string ShowAllLabel => string.Format(CultureInfo.CurrentCulture, Strings.Reference_ShowAll, Pairs.Count);

    /// <summary>"Show all 35 pairs for Lateral Beacon", the button's accessible name.</summary>
    public string ShowAllName => string.Format(CultureInfo.CurrentCulture, Strings.Reference_ShowAllPairsName, Pairs.Count, Feature.Name);

    /// <summary>Shows every pair.</summary>
    public ICommand ShowAllCommand { get; }

    private void ShowAll()
    {
        if (!CanShowAll)
            return;
        Visible = Pairs;
        _shown();
    }
}

/// <summary>
/// Finds a display setting's before/after pairs in the probe results
/// (R13c, #1722).
/// </summary>
/// <remarks>
/// <para>
/// An on/off setting a rule tests was probed both ways, so its pairs are
/// the combinations that differ only in it, off then on. For Simplified
/// symbols that is the paper-chart form beside the simplified one.
/// </para>
/// <para>
/// A number (safety contour, safety depth) was held at the catalogue's
/// default while the attributes compared with it were probed a step either
/// side. Its pairs are the combinations that differ only in such an
/// attribute, from just below the default to just above it. The rules
/// compare the two, so the same pair holds either side of the value set
/// now, and the caption says so.
/// </para>
/// <para>
/// A pair counts when both sides drew something other than the default
/// symbol and are shown by different glyphs; each pair of glyphs is listed
/// once per feature type, in the probe's order.
/// </para>
/// </remarks>
internal static class ReferencePairFinder
{
    /// <summary>The furthest from the default a probed number is taken as "either side" of it.</summary>
    public const double MaxStep = 1;

    /// <summary>The pairs of an on/off setting in <paramref name="probe"/>, off then on, as parts.</summary>
    /// <param name="probe">The feature type's probe.</param>
    /// <param name="setting">The setting's id.</param>
    /// <returns>The pairs, or none when the rule doesn't test it.</returns>
    public static IReadOnlyList<(string Before, string After)> Toggle(FeatureTypeProbe probe, string setting)
    {
        var input = ProbeOutcomes.InputOf(probe, ProbeInputKind.ContextParameter, setting);
        if (input < 0)
            return [];
        var off = ProbeOutcomes.ValueOf(probe.Inputs[input], "false");
        var on = ProbeOutcomes.ValueOf(probe.Inputs[input], "true");
        if (off < 0 || on < 0)
            return [];
        return [.. Pairs(probe, input, off, on).Select(p => (p.Before, p.After))];
    }

    /// <summary>
    /// The pairs of a number held at <paramref name="value"/> in
    /// <paramref name="probe"/>: for each attribute probed a step either
    /// side of it, below then above.
    /// </summary>
    /// <param name="probe">The feature type's probe.</param>
    /// <param name="value">The value the setting was probed at: its catalogue default.</param>
    /// <returns>The pairs, with the attribute and its two values.</returns>
    public static IReadOnlyList<(string Before, string After, string Attribute, double Below, double Above)> Either(FeatureTypeProbe probe, double value)
    {
        var found = new List<(string, string, string, double, double)>();
        for (var input = 0; input < probe.Inputs.Count; input++)
        {
            var values = probe.Inputs[input];
            if (values.Kind != ProbeInputKind.Attribute)
                continue;
            int below = -1, above = -1;
            double low = double.MinValue, high = double.MaxValue;
            for (var i = 0; i < values.Values.Count; i++)
            {
                if (!ProbeOutcomes.TryNumber(values.Values[i], out var n))
                    continue;
                if (n < value && n >= value - MaxStep && n > low)
                    (below, low) = (i, n);
                else if (n > value && n <= value + MaxStep && n < high)
                    (above, high) = (i, n);
            }

            if (below < 0 || above < 0)
                continue;
            foreach (var (before, after) in Pairs(probe, input, below, above))
            {
                if (!found.Any(f => f.Item1 == before && f.Item2 == after))
                    found.Add((before, after, values.Code, low, high));
            }
        }

        return found;
    }

    // The combinations with input at `from` against the same at `to`.
    private static List<(string Before, string After)> Pairs(FeatureTypeProbe probe, int input, int from, int to)
    {
        var pairs = new List<(string, string)>();
        var seen = new HashSet<(string, string)>();
        foreach (var variant in probe.GetVariants())
        {
            if (variant.Values[input] != from || variant.Outcome.IsError)
                continue;
            var other = variant.Values.ToArray();
            other[input] = to;
            if (probe.OutcomeOf(other) is not { IsError: false } then)
                continue;
            var before = ProbeOutcomes.MainPart(variant.Outcome);
            var after = ProbeOutcomes.MainPart(then);
            if (before is null || after is null || before == after || ProbeOutcomes.IsDefault(before) || ProbeOutcomes.IsDefault(after))
                continue;
            if (seen.Add((before, after)))
                pairs.Add((before, after));
        }

        return pairs;
    }
}
