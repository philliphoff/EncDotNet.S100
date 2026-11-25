using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Reads rule probe results (R13a) for entries: which of a probe's values a
/// picked feature has, the outcome of a combination, and the parts it draws
/// (symbol dictionary design note §7, R13b, #1721).
/// </summary>
internal static class ProbeValues
{
    /// <summary>The display setting a feature type's variant table switches between styles with.</summary>
    public const string SimplifiedSymbols = "SimplifiedSymbols";

    /// <summary>
    /// Each input's value for <paramref name="feature"/>, as an index into the
    /// input's <see cref="ProbeInput.Values"/>; <see langword="null"/> for an
    /// input whose value the feature doesn't have among those probed.
    /// </summary>
    /// <remarks>
    /// Listed and enumerated values, and display settings that are on or off,
    /// match exactly: colour 5 never maps to a probed 4. A real or integer
    /// attribute, or a numeric display setting, without an exact match takes
    /// the nearest value probed. A feature type alone, without a pick
    /// (<see cref="ReferenceFeature.IsPick"/> false), takes each attribute's
    /// first value probed, so nothing reads as left out.
    /// </remarks>
    /// <param name="probe">The feature type's probe.</param>
    /// <param name="index">The index, which maps a pick's S-57 acronyms to the rule's codes.</param>
    /// <param name="feature">The picked feature, or the feature type alone.</param>
    /// <param name="setting">
    /// A display setting's current value, invariant (<c>true</c>, <c>30</c>), or
    /// <see langword="null"/> to leave display settings unmatched.
    /// </param>
    /// <returns>An entry per input, in <see cref="FeatureTypeProbe.Inputs"/> order.</returns>
    public static int?[] IndexesOf(FeatureTypeProbe probe, ChartReferenceIndex index, ReferenceFeature feature, Func<string, string?>? setting = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(feature);
        var indexes = new int?[probe.Inputs.Count];
        for (int i = 0; i < probe.Inputs.Count; i++)
        {
            var input = probe.Inputs[i];
            indexes[i] = input.Kind switch
            {
                ProbeInputKind.Primitive => Find(input, PrimitiveOf(feature.Geometry), Same),
                ProbeInputKind.ContextParameter => setting?.Invoke(input.Code) is { } value
                    ? Find(input, value, Same) ?? Nearest(input, value)
                    : null,
                _ when !feature.IsPick => input.Values.Count > 0 ? 0 : null,
                _ => MatchAttribute(index, input, Values(index, feature, input.Code)),
            };
        }

        return indexes;
    }

    /// <summary>
    /// The outcome of the combination <paramref name="indexes"/> names, with
    /// <paramref name="input"/> set to <paramref name="value"/>; an input
    /// without a value takes its first.
    /// </summary>
    /// <param name="probe">The probe.</param>
    /// <param name="indexes">An index per input, or <see langword="null"/> for its first value.</param>
    /// <param name="input">The input to set, or -1 for none.</param>
    /// <param name="value">Its value's index.</param>
    /// <returns>The outcome, or <see langword="null"/> when the indexes don't name one.</returns>
    public static ProbeOutcome? OutcomeFor(FeatureTypeProbe probe, IReadOnlyList<int?> indexes, int input = -1, int value = 0)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(indexes);
        var values = new int[probe.Inputs.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = i == input ? value : i < indexes.Count ? indexes[i] ?? 0 : 0;
        return probe.OutcomeOf(values);
    }

    /// <summary>The input index of the Simplified symbols display setting, when the probe varies it; otherwise -1.</summary>
    /// <param name="probe">The probe.</param>
    public static int StyleInput(FeatureTypeProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        for (int i = 0; i < probe.Inputs.Count; i++)
        {
            var input = probe.Inputs[i];
            if (input is { Kind: ProbeInputKind.ContextParameter, Values.Count: > 1 }
                && string.Equals(input.Code, SimplifiedSymbols, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The value index of a context parameter input that is <paramref name="on"/>, or -1.</summary>
    /// <param name="input">The input.</param>
    /// <param name="on">Whether the setting is on.</param>
    public static int SettingValue(ProbeInput input, bool on)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = on ? "true" : "false";
        for (int i = 0; i < input.Values.Count; i++)
        {
            if (string.Equals(input.Values[i], text, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// A drawn part's kind and id: <c>symbol:BCNLAT21</c> is
    /// (<c>symbol</c>, <c>BCNLAT21</c>).
    /// </summary>
    /// <param name="part">The part, as <see cref="ProbeOutcome.Parts"/> holds it.</param>
    public static (string Kind, string Id) Split(string part)
    {
        ArgumentNullException.ThrowIfNull(part);
        int colon = part.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? (part, string.Empty) : (part[..colon], part[(colon + 1)..]);
    }

    /// <summary>
    /// The part an outcome is shown by: its first point symbol other than the
    /// catalogue's fallback, else its first plain colour fill, area fill or
    /// line style, else the fallback symbol or its first part;
    /// <see langword="null"/> when it draws nothing. DepthArea's safety
    /// contour pair is told apart by colour alone (<c>colour:DEPMD</c>,
    /// <c>colour:DEPDW</c>).
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    public static string? MainPart(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var symbol = outcome.Parts.FirstOrDefault(p => Split(p) is ("symbol", var id) && !LeadGlyph.DefaultSymbols.Contains(id));
        if (symbol is not null)
            return symbol;
        foreach (var kind in (string[])["colour", "fill", "line"])
        {
            var part = outcome.Parts.FirstOrDefault(p => Split(p).Kind == kind && !IsPlainLine(p));
            if (part is not null)
                return part;
        }

        return outcome.Parts.FirstOrDefault(p => Split(p).Kind == "symbol") ?? (outcome.Parts.Count > 0 ? outcome.Parts[0] : null);
    }

    /// <summary>The entry a part names: a symbol, line style or area fill; <see langword="null"/> for colour, pen and text.</summary>
    /// <param name="spec">The product.</param>
    /// <param name="part">The part.</param>
    public static ReferenceKey? KeyOf(Core.SpecRef spec, string part)
    {
        var (kind, id) = Split(part);
        if (id.Length == 0 || IsPlainLine(part))
            return null;
        return kind switch
        {
            "symbol" => new ReferenceKey(spec, ReferenceKind.Symbol, id),
            "line" => new ReferenceKey(spec, ReferenceKind.LineStyle, id),
            "fill" => new ReferenceKey(spec, ReferenceKind.AreaFill, id),
            _ => null,
        };
    }

    /// <summary>
    /// The feature's values of an attribute, joined with commas as a probe
    /// writes a multi-valued one (<c>3,4,3</c>); <see langword="null"/> when
    /// the feature doesn't have it.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="feature">The feature.</param>
    /// <param name="code">The rule's attribute code.</param>
    public static string? Values(ChartReferenceIndex index, ReferenceFeature feature, string code)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Attributes is not { } attributes)
            return null;
        var listed = PickTerms.Attribute(index, code) is { ListedValues.Count: > 0 };
        var values = new List<string>();
        var rows = Rows(index, attributes, code, topLevel: true).ToList();
        if (rows.Count == 0)
            rows = [.. Rows(index, attributes, code, topLevel: false)];
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.RawValue))
                continue;
            if (listed)
                values.AddRange(row.RawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            else
                values.Add(row.RawValue.Trim());
        }

        return values.Count == 0 ? null : string.Join(',', values);
    }

    // "line:_simple_" is a plain line the rule drew without a line style.
    private static bool IsPlainLine(string part) => part.StartsWith("line:_", StringComparison.Ordinal) || part.StartsWith("pen:", StringComparison.Ordinal);

    private static string? PrimitiveOf(S100GeometryType geometry) => geometry switch
    {
        S100GeometryType.Point => "Point",
        S100GeometryType.Curve => "Curve",
        S100GeometryType.Surface => "Surface",
        _ => null,
    };

    private static int? Find(ProbeInput input, string? value, Func<string?, string?, bool> same)
    {
        for (int i = 0; i < input.Values.Count; i++)
        {
            if (same(input.Values[i], value))
                return i;
        }

        return null;
    }

    // A value left out matches the feature without the attribute. A listed or
    // enumerated value matches exactly; a real or integer one ("2" and "2.0")
    // by value, else the nearest value probed.
    private static int? MatchAttribute(ChartReferenceIndex index, ProbeInput input, string? value)
    {
        var exact = Find(input, value, (probed, actual) => probed is null || actual is null
            ? probed is null && actual is null
            : string.Equals(probed, actual, StringComparison.Ordinal));
        if (exact is not null || value is null)
            return exact;
        var attribute = PickTerms.Attribute(index, input.Code);
        bool numeric = attribute is { ListedValues.Count: 0 }
            && attribute.ValueType.ToLowerInvariant() is "real" or "integer";
        return numeric ? Nearest(input, value) : null;
    }

    // The probed number nearest the value; null for a value that isn't a number.
    private static int? Nearest(ProbeInput input, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
            return null;
        int? best = null;
        var distance = double.PositiveInfinity;
        for (int i = 0; i < input.Values.Count; i++)
        {
            if (input.Values[i] is { } probed
                && double.TryParse(probed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && Math.Abs(number - target) < distance)
            {
                distance = Math.Abs(number - target);
                best = i;
            }
        }

        return best;
    }

    private static bool Same(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Top-level rows first; a sub-attribute's rows when no top-level row names it.
    private static IEnumerable<PickAttribute> Rows(ChartReferenceIndex index, IEnumerable<PickAttribute> rows, string code, bool topLevel)
    {
        foreach (var row in rows)
        {
            if (Names(index, row.Code, code))
                yield return row;
            if (!topLevel)
            {
                foreach (var child in Rows(index, row.Children, code, topLevel: false))
                    yield return child;
            }
        }
    }

    // The pick's code names the rule's: "colour", or an S-57 acronym with that alias, "COLOUR".
    private static bool Names(ChartReferenceIndex index, string pickCode, string ruleCode) =>
        string.Equals(pickCode, ruleCode, StringComparison.Ordinal)
        || (PickTerms.Attribute(index, pickCode) is { } entry && string.Equals(entry.Code, ruleCode, StringComparison.Ordinal));
}
