using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Sampling;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S421;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// Discriminated union over the typed payloads that a
/// <see cref="LoadedDataset"/> may carry. Each spec contributes either a
/// typed DataModel projection (vector products) or a coverage source
/// (HDF5-encoded coverage products).
/// </summary>
public abstract record LoadedDatasetData;

/// <summary>
/// S-101 Electronic Navigational Chart typed dataset.
/// </summary>
/// <remarks>
/// Carries the parsed <see cref="S101Dataset"/> handle. The MCP
/// <c>describe_feature</c> tool returns
/// <c>SpecNotSupportedForTool</c> for
/// S-101 today (see <c>S101FeatureDescriber</c>); the variant exists so
/// <c>list_datasets</c> can surface ENCs loaded into the viewer.
/// <para>
/// <c>ExternalTextResolver</c> is an optional file-name → text delegate
/// (backed by the dataset's exchange-set asset source) used to resolve a
/// feature's <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c>
/// attributes (S-101 Feature Catalogue) to their referenced text content,
/// so headless consumers (<c>identify_features</c> / <c>pick_features</c>)
/// see the same referenced text the viewer shows. <c>null</c> when the
/// dataset was loaded outside an exchange set.
/// </para>
/// <para>
/// <c>CompilationScale</c> is the source cell's compilation scale
/// denominator (S-57 DSPM CSCL) when the payload is a translated S-57 cell,
/// and <c>null</c> for a native S-101 or S-401 cell. The translation drops it
/// (S-101 has no compilation scale), so consumers that rank overlapping cells
/// with <see cref="DataModel.CellScaleRanking"/> read it from here, as the
/// display does from <c>VectorPortrayalResult.CellCompilationScale</c>.
/// </para>
/// </remarks>
public sealed record S101DatasetData(
    S101Dataset Dataset,
    Func<string, string?>? ExternalTextResolver = null,
    int? CompilationScale = null) : LoadedDatasetData
{
    /// <summary>
    /// A feature's highest drawing priority in the cell's latest portrayal,
    /// by feature id, or <c>null</c> when it isn't drawn; ranks
    /// <c>identify_features</c> matches as S-98 15.1 asks. <c>null</c> when
    /// the cell was loaded without a portrayal, which keeps the geometric order.
    /// </summary>
    public Func<string, int?>? DrawingPriority { get; init; }

    /// <summary>
    /// Runs the cell's portrayal rules for chosen features and reports the
    /// alerts they raise, for route checks (#1127); <c>null</c> when the
    /// payload was read without a portrayal catalogue.
    /// </summary>
    public S101AlertPortrayal? Alerts { get; init; }
}

/// <summary>S-122 Marine Protected Areas typed model.</summary>
public sealed record S122DatasetData(S122Dataset Model) : LoadedDatasetData;

/// <summary>S-124 Navigational Warnings typed model.</summary>
public sealed record S124DatasetData(S124Dataset Model) : LoadedDatasetData;

/// <summary>S-125 Marine Aids to Navigation typed model.</summary>
public sealed record S125DatasetData(S125Dataset Model) : LoadedDatasetData;

/// <summary>S-127 Marine Resources and Services typed model.</summary>
public sealed record S127DatasetData(S127Dataset Model) : LoadedDatasetData;

/// <summary>S-128 Catalogue of Nautical Products typed model.</summary>
public sealed record S128DatasetData(S128Dataset Model) : LoadedDatasetData;

/// <summary>S-129 Under Keel Clearance typed model.</summary>
public sealed record S129DatasetData(S129Dataset Model) : LoadedDatasetData;

/// <summary>S-131 Marine Harbour Infrastructure typed model.</summary>
public sealed record S131DatasetData(S131Dataset Model) : LoadedDatasetData;

/// <summary>S-201 Aids to Navigation Information typed model.</summary>
public sealed record S201DatasetData(S201Dataset Model) : LoadedDatasetData;

/// <summary>S-411 Sea Ice Information typed model.</summary>
public sealed record S411DatasetData(S411Dataset Model) : LoadedDatasetData;

/// <summary>S-421 Route Plans typed model.</summary>
public sealed record S421DatasetData(S421Dataset Model) : LoadedDatasetData;

/// <summary>S-102 Bathymetric Surface coverage handle.</summary>
/// <remarks>
/// Coverage variants carry a live handle (the <see cref="EncDotNet.S100.Pipelines.Coverage.ICoverageSource"/>
/// or its concrete equivalent). Tool implementations must treat the handle
/// as best-effort: it may be disposed between catalog capture and the
/// actual sample call. Wrap reads in
/// <c>try / catch (ObjectDisposedException)</c> and surface the failure as
/// <see cref="EncDotNet.S100.Datasets.Pipelines.Query.DatasetClosedDuringQuery"/>.
/// <para>
/// The coverage variants implement <see cref="ICoverageSampler"/>, the same
/// sampling their processors offer, which is how the <c>sample_coverage</c>
/// tools read them. A product with no variant of its own reaches the catalog
/// as a <see cref="ProductDatasetData"/> that also implements
/// <see cref="ICoverageSampler"/>.
/// </para>
/// </remarks>
public sealed record S102CoverageData(S102CoverageSource Source) : LoadedDatasetData, ICoverageSampler
{
    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => new S102CoverageSampler(Source).FindSites(query);
}

/// <summary>S-104 Water Level coverage handle (dcf2 — regular grid).</summary>
public sealed record S104CoverageData(S104CoverageSource Source) : LoadedDatasetData, ICoverageSampler
{
    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => new S104GridSampler(Source).FindSites(query);
}

/// <summary>
/// S-104 Water Level station-series dataset (dcf8 — time series at fixed
/// stations; see S-104 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public sealed record S104StationSeriesData(S104StationSeriesDataset Dataset) : LoadedDatasetData, ICoverageSampler
{
    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => new S104StationSampler(Dataset).FindSites(query);
}

/// <summary>S-111 Surface Currents coverage handle.</summary>
public sealed record S111CoverageData(S111CoverageSource Source) : LoadedDatasetData, ICoverageSampler
{
    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => new S111GridSampler(Source).FindSites(query);
}

/// <summary>
/// S-111 Surface Currents station-series dataset (dcf8 — time series at
/// fixed stations; see S-111 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public sealed record S111StationSeriesData(S111StationSeriesDataset Dataset) : LoadedDatasetData, ICoverageSampler
{
    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => new S111StationSampler(Dataset).FindSites(query);
}

/// <summary>
/// The payload of a product that has no built-in <see cref="LoadedDatasetData"/>
/// variant — a host's own product, or a non-S-100 format such as GRIB2 —
/// returned by its <see cref="ProductRegistration.OpenData"/>. It carries the
/// dataset's own <see cref="DatasetMetadata"/>, from which
/// <see cref="LoadedDatasetProjector"/> takes the catalog entry's spec, bounds
/// and time coverage, so the projector needs no case for the product. A
/// coverage product's payload also implements <see cref="ICoverageSampler"/>,
/// so the <c>sample_coverage</c> tools sample it with no tool changes.
/// </summary>
public abstract record ProductDatasetData : LoadedDatasetData
{
    /// <summary>The dataset's declared spec, extent and time coverage.</summary>
    public abstract DatasetMetadata Metadata { get; }
}
