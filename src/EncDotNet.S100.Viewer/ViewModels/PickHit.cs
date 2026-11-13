using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Resolved snapshot of a single feature hit produced by a pick gesture.
/// One <see cref="PickHit"/> is created per <see cref="Mapsui.Rendering.MapInfoRecord"/>
/// returned by <c>MapControl.GetMapInfo</c>; <see cref="PickReportViewModel"/>
/// shows the list of hits in the side panel and lets the user select which
/// one the attribute view describes.
/// </summary>
internal sealed class PickHit
{
    /// <summary>Picked feature's class/type code (e.g. "DepthArea").</summary>
    public required string FeatureType { get; init; }

    /// <summary>FC-resolved human-readable name of the feature type, when available.</summary>
    public string? FeatureTypeName { get; init; }

    /// <summary>Picked feature's dataset-specific reference identifier.</summary>
    public required string FeatureRef { get; init; }

    /// <summary>Display name (no path) of the dataset the feature came from.</summary>
    public string? DatasetFileName { get; init; }

    /// <summary>Product specification of the source dataset (e.g. "S-101").</summary>
    public string? ProductSpec { get; init; }

    /// <summary>
    /// The station's time series when this hit is a fixed-station observation
    /// (its <see cref="StationSeriesSection"/>); <c>null</c> for every other
    /// feature shape. Object Information charts it above the attribute list
    /// (#1401: the pick report builds the chart).
    /// </summary>
    public StationTimeSeriesSnapshot? StationSeries =>
        Sections.OfType<StationSeriesSection>().FirstOrDefault()?.Series;

    /// <summary>
    /// The hit's pick report content that isn't attribute rows: an ice egg code
    /// (<see cref="IceEggCodeSection"/>), a station's time series
    /// (<see cref="StationSeriesSection"/>), a navigational warning
    /// (<see cref="NavigationalWarningSection"/>) or a plug-in's own kind. The
    /// station chart and the warning cards show their kinds; the pick panel
    /// shows every other section through its kind's presenter
    /// (<see cref="PickSectionPresenters"/>).
    /// </summary>
    public IReadOnlyList<PickSection> Sections { get; init; } = [];

    /// <summary>
    /// The warning message when the hit is part of a navigational warning
    /// (its <see cref="NavigationalWarningSection"/>); <c>null</c> for every
    /// other feature. When set, the pick panel shows a Warning card above the
    /// attributes (#932).
    /// </summary>
    public NavigationalWarningInfo? NavigationalWarning =>
        Sections.OfType<NavigationalWarningSection>().FirstOrDefault()?.Warning;

    /// <summary>
    /// Whether the source dataset's exchange-set discovery metadata marks it
    /// not for navigation (<c>notForNavigation</c>), which labels the hit's
    /// long-text cards. False for a file opened without a catalogue.
    /// </summary>
    public bool NotForNavigation { get; init; }

    /// <summary>
    /// The provenance strip when the hit is a NATO Additional Military Layers
    /// feature (AML tag, cell, marking, drawn state; #1372); <see langword="null"/>
    /// for every other product.
    /// </summary>
    public AmlPickProvenance? Aml { get; init; }

    /// <summary>The data's credit from its Library source (#1075), "NOAA/NCEP GFS-Wave · Public domain"; null for none.</summary>
    public string? Credit { get; init; }

    /// <summary>
    /// The session id (<see cref="DatasetEntry.Id"/>) of the loaded dataset the
    /// hit came from, which finds the hit's Legend section and row (#1410);
    /// <see langword="null"/> when no loaded entry owns it.
    /// </summary>
    public string? DatasetId { get; init; }

    /// <summary>Attribute rows for the feature, decoded against the dataset's FC where available.</summary>
    public IReadOnlyList<PickAttribute> Attributes { get; init; } = [];

    /// <summary>
    /// xlink-style references this feature points to. Empty for products
    /// that do not model cross-references. Surfaced in the Object Info
    /// panel as clickable rows that re-open the panel on the target.
    /// </summary>
    public IReadOnlyList<FeatureReference> References { get; init; } = [];

    /// <summary>
    /// The feature's highest drawing priority in its dataset's portrayal
    /// (<see cref="FeatureInfo.DrawingPriority"/>), which orders the pick
    /// report; <c>null</c> when not known.
    /// </summary>
    public int? DrawingPriority { get; init; }

    /// <summary>The feature's geometry (<see cref="FeatureInfo.Geometry"/>), which orders the pick report at equal priority.</summary>
    public EncDotNet.S100.Features.S100GeometryType Geometry { get; init; }

    /// <summary>
    /// Processor that owns this hit's source feature. Set by
    /// <c>PickService</c> at construction time so reference navigation
    /// can re-query the same processor without re-walking the layer
    /// dictionary. <c>null</c> for unit-test fixtures that bypass the
    /// service.
    /// </summary>
    internal IDatasetProcessor? OwningProcessor { get; init; }

    /// <summary>
    /// Display name (no path) of the dataset that owns this hit, mirrored
    /// from <see cref="DatasetFileName"/> so reference-navigation hits
    /// can carry it without re-resolving the loader.
    /// </summary>
    internal string? OwningDatasetFileName => DatasetFileName;

    /// <summary>
    /// Label used in the hit-list row: prefers the FC-resolved name, falls
    /// back to the raw feature-type code when the FC could not decode it.
    /// </summary>
    public string DisplayLabel => FeatureTypeName ?? FeatureType;

    /// <summary>
    /// Mariner-facing instance name derived from the feature's attributes
    /// (e.g. "Number 10" from an S-101 <c>featureName</c>), or
    /// <see langword="null"/> when the feature carries no name attribute.
    /// </summary>
    public string? FeatureName => ViewModels.FeatureName.Derive(Attributes);

    /// <summary>
    /// Primary line for the hit-list row and identity block: the instance
    /// name when present, otherwise the feature class name.
    /// </summary>
    public string PrimaryLabel => FeatureName ?? DisplayLabel;

    /// <summary>
    /// Secondary line beneath <see cref="PrimaryLabel"/>: the feature class
    /// name when an instance name is leading, otherwise <see langword="null"/>
    /// (the class is already the primary line, so no subtitle is shown).
    /// </summary>
    public string? SecondaryLabel => FeatureName is not null ? DisplayLabel : null;

    /// <summary>True when <see cref="SecondaryLabel"/> has a value to show.</summary>
    public bool HasSecondaryLabel => SecondaryLabel is not null;

    /// <summary>
    /// Title of the hit-list row: a navigational warning's series and number
    /// (e.g. "NAVAREA X 106/26"), which tells two warnings apart where their
    /// class name cannot (#932); <see cref="PrimaryLabel"/> otherwise. The
    /// identity block keeps <see cref="PrimaryLabel"/>, since the Warning card
    /// beneath it already leads with the number.
    /// </summary>
    public string HitListLabel => NavigationalWarning?.Label ?? PrimaryLabel;

    /// <summary>Subtitle of the hit-list row: the class name when <see cref="HitListLabel"/> is not it.</summary>
    public string? HitListSecondaryLabel =>
        NavigationalWarning?.Label is not null ? DisplayLabel : SecondaryLabel;

    /// <summary>True when <see cref="HitListSecondaryLabel"/> has a value to show.</summary>
    public bool HasHitListSecondaryLabel => HitListSecondaryLabel is not null;

    /// <summary>
    /// Category glyph for the feature class, used as a fast visual scanning
    /// aid in the hit list and identity block.
    /// </summary>
    public FluentIcons.Common.Icon Glyph => Services.FeatureGlyphs.ForFeatureType(FeatureType);
}
