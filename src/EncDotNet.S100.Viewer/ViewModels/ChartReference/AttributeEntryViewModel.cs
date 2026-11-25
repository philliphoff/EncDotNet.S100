using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// An attribute's entry (design note §4.4, C3; R7, #1650): its definition,
/// each listed value with its definition, and the feature types whose
/// symbol it changes.
/// </summary>
/// <remarks>
/// Opened from a feature (a pick, or a link inside a feature type's entry)
/// whose rule was probed (R13a), each value also shows the symbol it gives
/// that feature type in both styles, and the picked feature's own value is
/// highlighted (R13c, #1722). The other inputs are held at the picked
/// feature's values and the display settings as they are now. Without a
/// feature, or without probe results, the entry lists the values alone.
/// </remarks>
internal sealed class AttributeEntryViewModel : ReferenceEntryViewModel
{
    /// <summary>Creates the entry view.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="context">The host.</param>
    public AttributeEntryViewModel(AttributeEntry attribute, ReferenceEntryContext context)
        : base(attribute, context, lead: null)
    {
        CodeLine = attribute.Alias is { Length: > 0 } alias
            ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_CodeWithAcronym, attribute.Code, alias)
            : attribute.Code;
        ValueType = string.Format(CultureInfo.CurrentCulture, Strings.Reference_ValueType, attribute.ValueType);
        Values = [.. attribute.ListedValues.Select(v => new ReferenceListedValue(v.Code, v.Label, v.Definition))];
        Changes =
        [
            .. attribute.ConditionedFeatureTypes
                .Select(code => LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.FeatureType, code)))
                .OfType<ChartReferenceLinkViewModel>()
                .OrderBy(l => l.Name, StringComparer.CurrentCulture),
        ];
        ValueSymbols = ValueSymbolsOf(attribute);
        if (ValueSymbols.Count > 0 && Index.TryGet(new ReferenceKey(Index.Spec, ReferenceKind.FeatureType, context.Feature!.FeatureType), out var feature))
            ValueSymbolsHeading = string.Format(CultureInfo.CurrentCulture, Strings.Reference_ValueSymbolsHeading, ChartReferenceRowViewModel.NameOf(feature));
    }

    /// <inheritdoc />
    public override string CodeLine { get; }

    /// <inheritdoc />
    public override string KindLabel => Strings.Reference_Kind_Attribute;

    /// <summary>"Value type: enumeration".</summary>
    public string ValueType { get; }

    /// <summary>The listed values, each with its definition.</summary>
    public IReadOnlyList<ReferenceListedValue> Values { get; }

    /// <summary>Whether the entry lists its values alone: it has listed values and no value symbols.</summary>
    public bool HasValues => Values.Count > 0 && !HasValueSymbols;

    /// <summary>
    /// Each value with the symbol it gives the feature the entry was opened
    /// from, in both styles (R13c); empty without a feature or probe results.
    /// </summary>
    public IReadOnlyList<ReferenceValueSymbols> ValueSymbols { get; }

    /// <summary>Whether the entry shows value symbols.</summary>
    public bool HasValueSymbols => ValueSymbols.Count > 0;

    /// <summary>"Drawn for Lateral Beacon".</summary>
    public string? ValueSymbolsHeading { get; }

    /// <summary>The feature types whose rules test it, so their symbol changes with it.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Changes { get; }

    /// <summary>Whether any rule tests it.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <inheritdoc />
    protected override IEnumerable<ReferenceGlyphTile> Tiles =>
        ValueSymbols.SelectMany(v => (ReferenceGlyphTile[])[v.Simplified, v.PaperChart]);

    // One row per listed value, then per value the probe tried that isn't a
    // listed value of its own (a list such as red, green, red; no value).
    private List<ReferenceValueSymbols> ValueSymbolsOf(AttributeEntry attribute)
    {
        if (Context.Feature is not { } feature
            || Index.Probes?.Find(feature.FeatureType) is not { } probe
            || ProbeOutcomes.InputOf(probe, ProbeInputKind.Attribute, attribute.Code) is not (>= 0 and var input))
        {
            return [];
        }

        var mariner = Context.Settings?.BuildMarinerSettings();
        var baseline = ProbeOutcomes.Baseline(
            probe,
            feature,
            mariner is null ? null : code => ReferenceTargets.Value(mariner, code),
            code => Index.TryGet(new ReferenceKey(Index.Spec, ReferenceKind.Attribute, code), out var e) && e is AttributeEntry { ListedValues.Count: > 0 });
        var style = ProbeOutcomes.InputOf(probe, ProbeInputKind.ContextParameter, ProbeOutcomes.SimplifiedSymbols);
        var on = style >= 0 ? ProbeOutcomes.ValueOf(probe.Inputs[style], "true") : -1;
        var off = style >= 0 ? ProbeOutcomes.ValueOf(probe.Inputs[style], "false") : -1;
        var values = probe.Inputs[input];
        var own = feature.IsPick ? ProbeOutcomes.ValueOf(values, feature.ValueOf(attribute.Code), nearest: false) : -1;
        var enumeration = attribute.ListedValues.Count > 0;

        ReferenceGlyphTile Tile(int value, int styleValue)
        {
            if (value < 0)
                return ReferenceGlyphTile.Of(outcome: null, Index, Context.Open);
            var combination = (int[])baseline.Clone();
            combination[input] = value;
            if (style >= 0 && styleValue >= 0)
                combination[style] = styleValue;
            return ReferenceGlyphTile.Of(probe.OutcomeOf(combination), Index, Context.Open);
        }

        ReferenceValueSymbols Row(string title, string? definition, int value) =>
            new(title, definition, value >= 0 && value == own, Tile(value, on), Tile(value, off));

        var rows = new List<ReferenceValueSymbols>();
        var shown = new HashSet<int>();
        foreach (var listed in attribute.ListedValues)
        {
            var value = ProbeOutcomes.ValueOf(values, listed.Code, nearest: false);
            if (value >= 0)
                shown.Add(value);
            rows.Add(Row(new ReferenceListedValue(listed.Code, listed.Label, listed.Definition).Title, listed.Definition, value));
        }

        for (var i = 0; i < values.Values.Count; i++)
        {
            if (shown.Contains(i))
                continue;
            var title = values.Values[i] is not { } probed
                ? Strings.Reference_NoValue
                : enumeration && attribute.ListedValuesOf(probed) is { } list
                    ? string.Join(", ", list.Select(v => v.Label))
                    : probed;
            rows.Add(Row(title, null, i));
        }

        return rows;
    }
}

/// <summary>One listed value of an attribute: "3 · red", with its definition.</summary>
/// <param name="Code">The numeric code.</param>
/// <param name="Label">The label.</param>
/// <param name="Definition">The definition, or <see langword="null"/>.</param>
internal sealed record ReferenceListedValue(string Code, string Label, string? Definition)
{
    /// <summary>"3 · red".</summary>
    public string Title => string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViewingGroupFormat, Code, Label);

    /// <summary>Whether there is a definition.</summary>
    public bool HasDefinition => !string.IsNullOrWhiteSpace(Definition);
}

/// <summary>
/// One value of an attribute with the symbol it gives a feature type, in
/// both styles (R13c, #1722).
/// </summary>
/// <param name="Title">"3 · red", "red, green, red" or "No value".</param>
/// <param name="Definition">The listed value's definition, or <see langword="null"/>.</param>
/// <param name="IsFeatureValue">Whether it is the picked feature's own value, which is highlighted.</param>
/// <param name="Simplified">The glyph with Simplified symbols on.</param>
/// <param name="PaperChart">The glyph with it off.</param>
internal sealed record ReferenceValueSymbols(
    string Title,
    string? Definition,
    bool IsFeatureValue,
    ReferenceGlyphTile Simplified,
    ReferenceGlyphTile PaperChart)
{
    /// <summary>Whether there is a definition.</summary>
    public bool HasDefinition => !string.IsNullOrWhiteSpace(Definition);

    /// <summary>"3 · red: simplified BCNLAT21, paper chart BCNSTK02, this feature's value".</summary>
    public string AccessibleName => string.Format(
        CultureInfo.CurrentCulture,
        IsFeatureValue ? Strings.Reference_ValueSymbolsNameOwn : Strings.Reference_ValueSymbolsName,
        Title,
        Simplified.Label,
        PaperChart.Label);
}
