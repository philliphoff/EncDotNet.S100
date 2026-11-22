namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// The kind of entry a <see cref="ReferenceKey"/> addresses in a
/// <see cref="ChartReferenceIndex"/>.
/// </summary>
public enum ReferenceKind
{
    /// <summary>A feature type from the feature catalogue.</summary>
    FeatureType,

    /// <summary>A point symbol from the portrayal catalogue, or a family of them.</summary>
    Symbol,

    /// <summary>A line style from the portrayal catalogue.</summary>
    LineStyle,

    /// <summary>An area fill from the portrayal catalogue.</summary>
    AreaFill,

    /// <summary>A simple attribute from the feature catalogue, with its listed values.</summary>
    Attribute,

    /// <summary>A portrayal context parameter, shown to the mariner as a display setting.</summary>
    DisplaySetting,
}
