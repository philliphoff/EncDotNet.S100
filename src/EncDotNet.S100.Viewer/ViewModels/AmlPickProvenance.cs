using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The provenance strip of a picked NATO Additional Military Layers (AML)
/// feature (#1372, design decision 13): the AML tag, the cell and the
/// protective marking, which replace the spec in the identity caption, plus
/// whether the map draws the feature.
/// </summary>
internal sealed class AmlPickProvenance
{
    /// <summary>The AML tag, "AML · LBO" or "AML 3.0 · SBO".</summary>
    public required string TagText { get; init; }

    /// <summary>The tag's tooltip: "Additional Military Layers · {product name} · {version}".</summary>
    public required string TagToolTip { get; init; }

    /// <summary>The cell (data set) name.</summary>
    public required string CellName { get; init; }

    /// <summary>The cell's protective marking, as given; <see langword="null"/> when it has none.</summary>
    public string? Marking { get; init; }

    /// <summary>True when <see cref="Marking"/> has a value to show.</summary>
    public bool HasMarking => !string.IsNullOrEmpty(Marking);

    /// <summary>Whether the feature's class is one the AML catalogue adds (<c>contct</c>), not a standard S-57 class.</summary>
    public required bool IsAmlClass { get; init; }

    /// <summary>Whether the map draws the feature; AML features are pickable either way.</summary>
    public required bool Drawn { get; init; }

    /// <summary>
    /// The class line under the heading: "AML class", "S-57 class with AML
    /// attributes" or "S-57 class".
    /// </summary>
    /// <param name="attributes">The feature's attributes, whose marks tell whether a standard class carries AML ones.</param>
    public string ClassKindText(IReadOnlyList<PickAttribute> attributes) =>
        IsAmlClass
            ? Strings.Pick_AmlClass
            : attributes.Any(a => a.Mark == PickAttribute.AmlMark)
                ? Strings.Pick_AmlStandardClassWithAml
                : Strings.Pick_AmlStandardClass;

    /// <summary>Builds the provenance of <paramref name="info"/>, picked from <paramref name="processor"/>.</summary>
    /// <param name="processor">The AML cell's processor.</param>
    /// <param name="info">The picked feature's information.</param>
    public static AmlPickProvenance From(AmlDatasetProcessor processor, FeatureInfo info)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(info);
        var aml = processor.Info;
        return new AmlPickProvenance
        {
            TagText = aml.TagText,
            TagToolTip = aml.TagToolTip,
            CellName = aml.DataSetName,
            Marking = aml.ProtectiveMarking,
            IsAmlClass = processor.Cell.IsAmlClass(info.FeatureType),
            Drawn = info.DrawingPriority is not null,
        };
    }
}
