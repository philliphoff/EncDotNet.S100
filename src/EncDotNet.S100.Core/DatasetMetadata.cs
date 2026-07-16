using System.ComponentModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Core;

/// <summary>
/// Lightweight, product-agnostic description of a dataset obtained
/// <em>without</em> fully reading or portraying it — the output of a
/// dataset reader's <c>ReadMetadata</c> "peek" path. Carries only the
/// facts a host needs to place a dataset on the map and decide whether it
/// is worth loading in full: its declared specification, geographic
/// extent, and (where the encoding models them) display-scale window and
/// temporal coverage.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to make dataset loading <em>phased</em>: a host can
/// probe many datasets cheaply — frame a viewport from the union of their
/// extents, register them in a layer list, draw an out-of-scale indicator
/// — and defer the expensive full parse + portrayal of each dataset until
/// the user actually brings it into view. For a catalogued exchange set
/// the same facts come from <c>CATALOG.XML</c>; for a "loose" folder of
/// datasets they must come from the datasets themselves, which is what a
/// reader's <c>ReadMetadata</c> supplies.
/// </para>
/// <para>
/// Only <see cref="Spec"/> is guaranteed. <see cref="Extent"/>,
/// <see cref="DisplayScale"/>, and <see cref="TimeCoverage"/> are optional
/// because they are not universal across S-100 encodings: display scale is
/// an ENC / catalogue concept, temporal coverage exists only for dynamic
/// products (the HDF5 time-series products S-104 and S-111), and some
/// encodings (notably GML without a reliable <c>gml:boundedBy</c>) cannot
/// yield an extent cheaply. A
/// <c>null</c> optional means "not cheaply available" — the host should
/// fall back to a full load rather than treat it as authoritative.
/// </para>
/// <para>
/// A probed <see cref="Extent"/> is an <em>estimate</em>: the authoritative
/// extent (which may include portrayal-induced symbol overhang) is the one
/// produced by the full render. Hosts should overwrite the probe estimate
/// once the dataset is fully loaded.
/// </para>
/// </remarks>
public sealed record DatasetMetadata
{
    /// <summary>
    /// The product specification (name + edition) the dataset declares
    /// conformance to, resolved from the dataset itself (HDF5
    /// <c>productSpecification</c> attribute, GML application namespace,
    /// S-101 <c>ProductSpecificationEdition</c>, etc.). Always present. The
    /// edition is <c>default</c> (<c>0.0.0</c>) when the dataset does not
    /// self-describe a parseable edition — callers should not assume a real
    /// supported edition is always present.
    /// </summary>
    public required SpecRef Spec { get; init; }

    /// <summary>
    /// The dataset's geographic extent, or <c>null</c> when it cannot be
    /// determined without a full parse. Expressed in the coordinate system
    /// identified by <see cref="HorizontalCrsEpsg"/> — i.e. WGS-84 decimal
    /// degrees (lat/lon) when that is <c>null</c>, otherwise the projected
    /// units of that CRS. Consumers framing a map viewport must reproject
    /// through <see cref="HorizontalCrsEpsg"/> before use.
    /// </summary>
    /// <remarks>
    /// <see cref="BoundingBox"/> nominally documents itself as WGS-84; for
    /// projected HDF5 grids (e.g. UTM S-102, S-100 Part 10c) the edge values
    /// are the grid's native easting/northing bounds, mirroring the existing
    /// coverage-source extent convention. <see cref="HorizontalCrsEpsg"/>
    /// disambiguates the two cases.
    /// </remarks>
    public BoundingBox? Extent { get; init; }

    /// <summary>
    /// EPSG code of the coordinate reference system in which
    /// <see cref="Extent"/> is expressed, or <c>null</c> to mean EPSG:4326
    /// (WGS-84 geographic). Populated for HDF5 gridded products that may be
    /// projected (e.g. UTM S-102); <c>null</c> for GML / ENC vector products
    /// whose geometry is always geographic.
    /// </summary>
    public int? HorizontalCrsEpsg { get; init; }

    /// <summary>
    /// The display-scale window (coarsest / finest scale denominators) at
    /// which the dataset is intended to draw, or <c>null</c> for encodings
    /// that do not carry one. Sourced from the ENC <c>DataCoverage</c>
    /// (S-101 FC §3.1.1) or an equivalent catalogue field.
    /// </summary>
    public DisplayScaleRange? DisplayScale { get; init; }

    /// <summary>
    /// The temporal span the dataset covers, or <c>null</c> for static
    /// products. Populated for the HDF5 time-series products (S-104 and
    /// S-111), whose forecast / observation time dimension is read from the
    /// group time attributes without touching the payload. Other encodings
    /// that carry time (e.g. GML S-411) leave this <c>null</c> because it is
    /// not surfaced cheaply today.
    /// </summary>
    public TimeCoverage? TimeCoverage { get; init; }
}

/// <summary>
/// A display-scale window expressed as denominator bounds. The
/// <see cref="Minimum"/> denominator is the <em>coarsest</em> scale
/// (largest number) at which content is intended to display; the
/// <see cref="Maximum"/> denominator is the <em>finest</em> (smallest
/// number). Either bound may be <c>null</c> when only one end is specified.
/// Mirrors the S-101 <c>DataCoverage</c> <c>minimumDisplayScale</c> /
/// <c>maximumDisplayScale</c> pair (S-101 FC §3.1.1; S-100 Part 17).
/// </summary>
/// <param name="Minimum">Coarsest display-scale denominator (largest value), or <c>null</c>.</param>
/// <param name="Maximum">Finest display-scale denominator (smallest value), or <c>null</c>.</param>
[Description("Display-scale window as coarsest/finest scale denominators.")]
public readonly record struct DisplayScaleRange(int? Minimum, int? Maximum);

/// <summary>
/// The temporal span a dynamic dataset covers, from its earliest to its
/// latest time step (inclusive). Used to gate a dataset's visibility
/// against a global clock without reading its per-step payload.
/// </summary>
/// <param name="Start">Earliest covered instant (UTC).</param>
/// <param name="End">Latest covered instant (UTC).</param>
[Description("Inclusive temporal span [Start, End] of a dynamic dataset.")]
public readonly record struct TimeCoverage(DateTime Start, DateTime End);
