using System.Collections.Generic;
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

    /// <summary>Attribute rows for the feature, decoded against the dataset's FC where available.</summary>
    public IReadOnlyList<PickAttribute> Attributes { get; init; } = [];

    /// <summary>
    /// Label used in the hit-list row: prefers the FC-resolved name, falls
    /// back to the raw feature-type code when the FC could not decode it.
    /// </summary>
    public string DisplayLabel => FeatureTypeName ?? FeatureType;
}
