using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// An attribute's entry (design note §4.4, C3; R7, #1650): its definition,
/// each listed value with its definition, and the feature types whose
/// symbol it changes.
/// </summary>
/// <remarks>
/// The symbol each value gives a feature, in both styles, waits for rule
/// probing (R13).
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
    }

    /// <inheritdoc />
    public override string CodeLine { get; }

    /// <inheritdoc />
    public override string KindLabel => Strings.Reference_Kind_Attribute;

    /// <summary>"Value type: enumeration".</summary>
    public string ValueType { get; }

    /// <summary>The listed values, each with its definition.</summary>
    public IReadOnlyList<ReferenceListedValue> Values { get; }

    /// <summary>Whether it has listed values.</summary>
    public bool HasValues => Values.Count > 0;

    /// <summary>The feature types whose rules test it, so their symbol changes with it.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Changes { get; }

    /// <summary>Whether any rule tests it.</summary>
    public bool HasChanges => Changes.Count > 0;
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
