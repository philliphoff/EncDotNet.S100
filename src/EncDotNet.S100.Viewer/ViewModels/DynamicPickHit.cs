using System;
using System.Collections.Generic;
using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Resolved snapshot of a single dynamic-feature hit produced by a pick
/// gesture. Sibling type to <see cref="PickHit"/>: dataset-owned features
/// flow through <c>PickHit</c>, dynamic-source features flow through
/// <see cref="DynamicPickHit"/>. The pick report panel renders the two
/// lists in adjacent sections.
/// </summary>
/// <remarks>
/// The dataset and dynamic identity models are different enough — opaque
/// string id vs FC-bound feature ref, mutable position vs static
/// coordinates, free-form attribute dictionary vs FC-decoded attribute
/// list, no xlink references — that we keep the types separate rather
/// than threading optional fields through <see cref="PickHit"/>. See
/// <c>docs/design/dynamic-source-pick.md</c> §1 Q1.
/// </remarks>
internal sealed record DynamicPickHit
{
    /// <summary>Source instance id (e.g. <c>"ownship"</c>, <c>"ais.aisstream"</c>).</summary>
    public required string SourceId { get; init; }

    /// <summary>Human-readable source name from <see cref="DynamicSourceMetadata.DisplayName"/>.</summary>
    public required string SourceDisplayName { get; init; }

    /// <summary>Source-stable feature id from <see cref="DynamicFeature.Id"/>.</summary>
    public required string FeatureId { get; init; }

    /// <summary>Renderer-dispatch hint from <see cref="DynamicFeature.Kind"/>.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Display label preferred over <see cref="FeatureId"/> in the hit
    /// list. Picks the AIS vessel name when present, else the feature id.
    /// </summary>
    public required string DisplayLabel { get; init; }

    /// <summary>UTC of the most recent feature update.</summary>
    public required DateTimeOffset LastUpdated { get; init; }

    /// <summary>WGS-84 latitude of the picked point.</summary>
    public required double Latitude { get; init; }

    /// <summary>WGS-84 longitude of the picked point.</summary>
    public required double Longitude { get; init; }

    /// <summary>Optional motion sidecar (COG / heading / SOG).</summary>
    public DynamicMotion? Motion { get; init; }

    /// <summary>Optional vessel-geometry sidecar (length / beam).</summary>
    public DynamicVesselGeometry? VesselGeometry { get; init; }

    /// <summary>Source-defined attribute rows (vessel name, MMSI, …).</summary>
    public IReadOnlyList<DynamicPickAttributeRow> Attributes { get; init; }
        = Array.Empty<DynamicPickAttributeRow>();
}

/// <summary>
/// Single row in a <see cref="DynamicPickHit.Attributes"/> dump. Both the
/// label and the formatted value are pre-localised by the pick service
/// so the view binds them as plain strings.
/// </summary>
internal sealed record DynamicPickAttributeRow(string Label, string Value);
