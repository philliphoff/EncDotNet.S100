using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Processes a dataset file and renders it into Mapsui layers.
/// Constructed once per file; <see cref="Render"/> may be called
/// multiple times with different spec-specific contexts.
/// </summary>
public interface IDatasetProcessor
{
    string ProductSpec { get; }

    DatasetResult Render(RenderContext? context = null);

    /// <summary>
    /// Returns information about a feature identified by its reference string
    /// (as stored in the Mapsui feature via <c>FeatureRefKey</c>), or <c>null</c>
    /// if the feature cannot be found.
    /// </summary>
    /// <remarks>
    /// Real-world datasets occasionally contain features that share a
    /// <c>gml:id</c> (a producer bug), so callers that already know the
    /// feature's position within the dataset should prefer
    /// <see cref="GetFeatureInfoAt"/> for a collision-free lookup.
    /// </remarks>
    FeatureInfo? GetFeatureInfo(string featureRef);

    /// <summary>
    /// Returns information about the feature at the given enumeration
    /// ordinal — the same index reported via
    /// <see cref="FeatureSummary.Ordinal"/> from
    /// <see cref="EnumerateFeatures"/>. Used by the search index so
    /// duplicate <c>gml:id</c>s (a real producer bug) still route to
    /// the correct feature.
    /// </summary>
    /// <returns>
    /// Feature info, or <c>null</c> when the ordinal is out of range or
    /// this processor does not expose discrete features.
    /// </returns>
    FeatureInfo? GetFeatureInfoAt(int ordinal) => null;

    /// <summary>
    /// Samples this dataset at the supplied geographic position and
    /// returns a synthetic <see cref="FeatureInfo"/> describing the
    /// underlying coverage value(s) — used by the viewer to provide a
    /// pick experience on raster / coverage products (S-102, S-104,
    /// S-111). Vector products do not implement this method and the
    /// default implementation returns <c>null</c>.
    /// </summary>
    /// <param name="latitude">Click position latitude in WGS84 degrees.</param>
    /// <param name="longitude">Click position longitude in WGS84 degrees.</param>
    /// <param name="time">
    /// The selected time step for time-aware coverages (S-104, S-111);
    /// ignored by static coverages (S-102). When <c>null</c> the
    /// implementation chooses the dataset's first available time step.
    /// </param>
    /// <returns>
    /// A <see cref="FeatureInfo"/> whose <see cref="FeatureInfo.Attributes"/>
    /// carry the sampled coverage values, or <c>null</c> if the click
    /// falls outside the dataset extent or this processor does not
    /// expose coverage data.
    /// </returns>
    FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time) => null;

    /// <summary>
    /// Enumerates a lightweight summary of every feature exposed by this
    /// processor — used to seed the viewer's feature-search index without
    /// forcing a full <see cref="GetFeatureInfo"/> call per feature.
    /// </summary>
    /// <remarks>
    /// Coverage products (S-102, S-104, S-111) and any processor that does
    /// not expose discrete features should leave this returning the
    /// default empty enumeration.
    /// </remarks>
    IEnumerable<FeatureSummary> EnumerateFeatures() => System.Array.Empty<FeatureSummary>();
}

/// <summary>
/// Lightweight feature descriptor returned by
/// <see cref="IDatasetProcessor.EnumerateFeatures"/>.
/// </summary>
public sealed class FeatureSummary
{
    /// <summary>The feature reference string (dataset-specific ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// The feature's position within
    /// <see cref="IDatasetProcessor.EnumerateFeatures"/>. Used as a
    /// collision-free key when a producer reuses <c>gml:id</c>s across
    /// features within a single dataset; pair with
    /// <see cref="IDatasetProcessor.GetFeatureInfoAt"/> to resolve.
    /// Defaults to 0 — processors must set this to the loop index when
    /// duplicate-id resilience matters.
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// The feature type code as it appears in the dataset (typically the
    /// GML element local name or the feature catalogue code).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>
    /// Human-readable feature type name resolved through the dataset's
    /// Feature Catalogue when available; otherwise <c>null</c>.
    /// </summary>
    public string? FeatureTypeName { get; init; }
}

/// <summary>
/// Feature information returned by a pick / object-info operation.
/// </summary>
public sealed class FeatureInfo
{
    /// <summary>The feature reference string (dataset-specific ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// The feature type code as it appears in the dataset (typically the
    /// GML element local name or the feature catalogue code, e.g.
    /// <c>"DepthArea"</c>, <c>"LateralBuoy"</c>, <c>"NavwarnPart"</c>).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>
    /// Human-readable feature type name resolved through the dataset's
    /// Feature Catalogue, when one is available; otherwise <c>null</c>.
    /// Viewers should prefer this over <see cref="FeatureType"/> when set.
    /// </summary>
    public string? FeatureTypeName { get; init; }

    /// <summary>
    /// Feature attributes, optionally decoded against the dataset's
    /// Feature Catalogue (see <see cref="PickAttribute"/>). Complex
    /// attributes nest their sub-attributes via
    /// <see cref="PickAttribute.Children"/>.
    /// </summary>
    public required IReadOnlyList<PickAttribute> Attributes { get; init; }

    /// <summary>
    /// xlink-style references this feature points to (e.g. S-125
    /// information bindings, S-421 route topology). Each reference's
    /// <see cref="FeatureReference.TargetRef"/> resolves against the
    /// same dataset's feature collection via
    /// <see cref="IDatasetProcessor.GetFeatureInfo"/>. Empty for
    /// products that do not model cross-references.
    /// </summary>
    public IReadOnlyList<FeatureReference> References { get; init; }
        = System.Array.Empty<FeatureReference>();
}

/// <summary>
/// An xlink-style reference from one feature to another within the same
/// dataset. Promoted from per-spec reference types (e.g.
/// <c>S125InformationReference</c>, <c>S421Reference</c>) so that the
/// pick UI can offer uniform "follow reference" navigation across all
/// GML-encoded products.
/// </summary>
public sealed class FeatureReference
{
    /// <summary>
    /// The role / association name as it appears in the dataset (e.g.
    /// <c>"AtonStatus"</c> for an S-125 information binding, or
    /// <c>"routeWaypoints"</c> for an S-421 route → waypoints link).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The referenced feature's identifier (matches
    /// <see cref="FeatureInfo.FeatureRef"/> on the target). Already
    /// trimmed of any leading <c>#</c> from the underlying
    /// <c>xlink:href</c>.
    /// </summary>
    public required string TargetRef { get; init; }

    /// <summary>
    /// The <c>xlink:arcrole</c> value when present; conveys the semantic
    /// of the link (e.g. an S-421 segment role) and may be null.
    /// </summary>
    public string? ArcRole { get; init; }
}
