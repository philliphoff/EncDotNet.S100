using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Full time-series snapshot for a single fixed station, projected out of
/// the per-product station model (S-104 <c>WaterLevelStation</c>,
/// S-111 <c>SurfaceCurrentStation</c>) into a product-agnostic shape that
/// the viewer can render in a chart without referencing the per-product
/// dataset assemblies.
/// </summary>
/// <remarks>
/// Carries the raw sample values — including any sentinel fill-value
/// samples — so the viewer can decide how to filter them; the pipeline
/// keeps the underlying station model intact.
/// </remarks>
public sealed class StationTimeSeriesSnapshot
{
    /// <summary>Dataset-specific station identifier (S-10X
    /// <c>stationIdentification</c>).</summary>
    public required string StationId { get; init; }

    /// <summary>Optional human-readable station name (may equal
    /// <see cref="StationId"/> when the dataset does not encode a separate
    /// name).</summary>
    public string? StationName { get; init; }

    /// <summary>Station latitude in decimal degrees (WGS-84).</summary>
    public required double Latitude { get; init; }

    /// <summary>Station longitude in decimal degrees (WGS-84).</summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// UTC timestamps of every sample, in ascending chronological order.
    /// Same length as every <see cref="StationTimeSeriesChannel.Values"/>
    /// array on <see cref="Channels"/>.
    /// </summary>
    public required IReadOnlyList<DateTime> Times { get; init; }

    /// <summary>
    /// One channel per measured variable (e.g. height for S-104; speed and
    /// direction for S-111). Each channel's <c>Values</c> array is the same
    /// length as <see cref="Times"/>.
    /// </summary>
    public required IReadOnlyList<StationTimeSeriesChannel> Channels { get; init; }
}

/// <summary>
/// A single value series within a <see cref="StationTimeSeriesSnapshot"/>.
/// </summary>
public sealed class StationTimeSeriesChannel
{
    /// <summary>
    /// Stable channel key, used by viewers to identify the channel without
    /// matching on the (localisable) <see cref="DisplayName"/>. Convention:
    /// lower-camel-case attribute code from the source feature catalogue
    /// (e.g. <c>"waterLevelHeight"</c>, <c>"surfaceCurrentSpeed"</c>,
    /// <c>"surfaceCurrentDirection"</c>).
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// English display name for the channel as produced by the pipeline.
    /// The viewer typically replaces this with a localised string keyed
    /// off <see cref="Key"/>.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Canonical unit string for the channel's values (e.g. <c>"m"</c>,
    /// <c>"m/s"</c>, <c>"°"</c>). Pipeline-supplied for diagnostics; the
    /// viewer may substitute a localised axis label.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Sample values, one per entry in
    /// <see cref="StationTimeSeriesSnapshot.Times"/>. May include sentinel
    /// fill values (e.g. <c>-9999</c>); see <see cref="FillValue"/>.
    /// </summary>
    public required IReadOnlyList<float> Values { get; init; }

    /// <summary>
    /// Sentinel value the producer used to mark missing samples (typically
    /// <c>-9999</c>). When set, viewers should drop samples equal to this
    /// value from charts. <c>null</c> when the channel has no fill marker.
    /// </summary>
    public float? FillValue { get; init; }
}
