using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Builds an <see cref="IDatasetProcessor"/> for a dataset on the local file
/// system, given the shared <paramref name="services"/> and the dataset
/// <paramref name="path"/>.
/// </summary>
public delegate IDatasetProcessor DatasetProcessorFromPath(
    DatasetProcessorServices services,
    string path);

/// <summary>
/// Builds an <see cref="IDatasetProcessor"/> for a dataset stored inside an
/// <see cref="IAssetSource"/> (e.g. an exchange-set folder or ZIP), given the
/// shared <paramref name="services"/> and the <paramref name="request"/>.
/// </summary>
public delegate IDatasetProcessor DatasetProcessorFromSource(
    DatasetProcessorServices services,
    DatasetProcessorSourceRequest request);

/// <summary>
/// Opens a dataset's bytes into the typed <see cref="LoadedDatasetData"/>
/// payload a <see cref="LoadedDataset"/> carries. The caller owns
/// <paramref name="stream"/> and disposes it.
/// </summary>
/// <param name="stream">The dataset bytes.</param>
/// <param name="externalTextResolver">
/// Optional file-name → text delegate for products that reference external
/// text files (S-101 <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c>);
/// products that don't ignore it.
/// </param>
public delegate LoadedDatasetData DatasetDataOpener(
    Stream stream,
    Func<string, string?>? externalTextResolver);

/// <summary>
/// A dataset stored inside an <see cref="IAssetSource"/>, addressed for
/// processor construction by a <see cref="DatasetProcessorFromSource"/>.
/// </summary>
public sealed record DatasetProcessorSourceRequest
{
    /// <summary>The asset source (folder or ZIP) hosting the dataset.</summary>
    public required IAssetSource Source { get; init; }

    /// <summary>Path to the dataset, relative to <see cref="Source"/>.</summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Optional map of support-file name (case-insensitive) to source-relative
    /// path, from the exchange-set catalogue (S-100 Edition 5.2.1 Part 17).
    /// </summary>
    public IReadOnlyDictionary<string, string>? SupportFiles { get; init; }
}

/// <summary>
/// Registers one product with a <see cref="ProductRegistry"/>: the canonical
/// spec string it handles (e.g. <c>"S-101"</c>, or a non-S-100 format name
/// such as <c>"GRIB2"</c>), the two ways to construct its processor (from a
/// file path, and from an <see cref="EncDotNet.S100.Core.IAssetSource"/>), how
/// to recognise its files, and the product traits hosts need to treat it
/// correctly. The traits replace the <c>switch</c>es on spec names that used to
/// be spread across the hosts (metadata reading, catalog projection, default
/// S-98 planes, portrayal labelling), so a new product is one registration plus
/// one processor and the hosts don't change.
/// </summary>
/// <remarks>
/// The registration lives where the product's types are visible (the built-in
/// ones in <see cref="S100Products"/>), so the delegates
/// (<see cref="ReadMetadata"/>, <see cref="OpenData"/>, the matchers) close over
/// the product's own readers and the hosts only ever see the delegates.
/// </remarks>
public sealed record ProductRegistration
{
    /// <summary>
    /// The <see cref="DefaultPlanes"/> key that applies to every layer kind
    /// without an entry of its own.
    /// </summary>
    public const string AnyLayerKind = "*";

    /// <summary>The canonical product-specification string (e.g. <c>"S-101"</c>).</summary>
    public required string Spec { get; init; }

    /// <summary>Constructs the product's processor from a local file path.</summary>
    public required DatasetProcessorFromPath CreateFromPath { get; init; }

    /// <summary>Constructs the product's processor from an asset source.</summary>
    public required DatasetProcessorFromSource CreateFromSource { get; init; }

    /// <summary>
    /// Whether the product is an IHO S-100 product (or legacy S-57). Hosts label
    /// a product that isn't — a weather model grid, say — as "Not an S-100
    /// product" and warn that it isn't for navigation (see
    /// <see cref="PickReportNotice"/>). Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsS100 { get; init; } = true;

    /// <summary>
    /// What the product is drawn from: an S-100 portrayal catalogue
    /// (<see cref="PortrayalBasis.Catalogue"/>, the default), or a portrayal
    /// this library defines in code because the specification defines none
    /// (<see cref="PortrayalBasis.Application"/>, e.g. S-104 Ed 2.0.0). Hosts
    /// label an application portrayal wherever they describe the portrayal.
    /// </summary>
    public PortrayalBasis Portrayal { get; init; } = PortrayalBasis.Catalogue;

    /// <summary>
    /// The specification whose Feature Catalogue, Portrayal Catalogue and ECDIS
    /// display conventions apply to the product, when it isn't the product's
    /// own: <c>"S-101"</c> for legacy S-57, which is translated in memory and
    /// drawn through the S-101 catalogue. <see langword="null"/> (the default)
    /// means the product's own <see cref="Spec"/>.
    /// </summary>
    public string? PortrayalSpec { get; init; }

    /// <summary>
    /// Optional cheap metadata reader: given a dataset path, returns its
    /// <see cref="DatasetMetadata"/> (declared spec, extent, CRS, time) without
    /// a full parse. The viewer's Library uses it to index loose datasets; a
    /// product without one can't be indexed without loading it. It may throw on
    /// an unreadable file; callers treat that as "not available".
    /// </summary>
    public Func<string, DatasetMetadata>? ReadMetadata { get; init; }

    /// <summary>
    /// Optional opener for the product's typed catalog payload, used by
    /// <see cref="LoadedDatasetProjector"/> to project a dataset's bytes into a
    /// <see cref="LoadedDataset"/>. <see langword="null"/> for a product with no
    /// catalog projection; a product whose payload isn't one of the built-in
    /// <see cref="LoadedDatasetData"/> variants returns a
    /// <see cref="ProductDatasetData"/>.
    /// </summary>
    public DatasetDataOpener? OpenData { get; init; }

    /// <summary>
    /// The default S-98 display plane for each layer kind the product emits,
    /// keyed by layer kind (case-insensitive), with <see cref="AnyLayerKind"/>
    /// for every other kind. Only the fallback for callers that ask the
    /// <see cref="Interoperability.IDisplayPlaneAuthority"/> without a sub-layer
    /// in hand: sub-layers carry their own plane. Empty (the default) puts the
    /// product on <see cref="S98DisplayPlane.OtherChartOverlays"/>.
    /// </summary>
    public IReadOnlyDictionary<string, S98DisplayPlane> DefaultPlanes { get; init; } =
        new Dictionary<string, S98DisplayPlane>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The first line of a pick report for one of the product's features, when
    /// the product needs one; e.g. a weather model's "Weather model data, not
    /// for navigation.". <see langword="null"/> (the default) lets the host
    /// choose: the viewer shows its own localised "Weather model data, not for
    /// navigation." for a product that isn't <see cref="IsS100"/>, and nothing
    /// for an S-100 product.
    /// </summary>
    public string? PickReportNotice { get; init; }

    /// <summary>
    /// Optional recognizer for an ISO 8211-encoded product: given the parsed
    /// envelope of a <c>.000</c> dataset, decides whether the file belongs to
    /// this product. The <c>.000</c> extension is shared by legacy S-57, S-101,
    /// and S-401 (inland ENC), so the extension alone cannot route the file.
    /// This inverts the former central S-57-vs-S-101 sniff in
    /// <see cref="DatasetPipelineFactory"/> into data, so a new ISO 8211 product
    /// is recognized in the same place it is constructed — the ISO 8211 sibling
    /// of <see cref="MatchGml"/>. It is <see langword="null"/> for products whose
    /// extension already identifies them. <see cref="DatasetPipelineFactory"/>
    /// reads the envelope once and returns the spec of the first registered
    /// product whose matcher claims it, so a host that omits S-57 never
    /// misroutes a cell to a product it cannot build.
    /// </summary>
    public DatasetIso8211Matcher? MatchIso8211 { get; init; }

    /// <summary>
    /// Optional recognizer for a GML-encoded product: given the parsed root of a
    /// <c>.gml</c> document, decides whether the document belongs to this
    /// product. This is what inverts the former central GML namespace
    /// <c>switch</c> in <see cref="DatasetPipelineFactory"/> into data, so a new
    /// GML product is recognized in the same place it is constructed. It is
    /// <see langword="null"/> for non-GML products (S-101/S-57 ISO 8211,
    /// S-102/S-104/S-111 HDF5), whose extension or header identifies them
    /// instead. <see cref="DatasetPipelineFactory"/> reads the document root once
    /// and returns the spec of the first registered product whose matcher claims
    /// it.
    /// </summary>
    public DatasetGmlMatcher? MatchGml { get; init; }

    /// <summary>
    /// Optional recognizer for an HDF5-encoded product: given the root group of
    /// an HDF5 file (its <c>productSpecification</c> attribute, and any other
    /// root attribute it wants to read), decides whether the file belongs to
    /// this product. The HDF5 sibling of <see cref="MatchIso8211"/> and
    /// <see cref="MatchGml"/>: S-102, S-104 and S-111 each claim the files that
    /// declare them, and an HDF5 file nobody claims is unknown.
    /// </summary>
    public DatasetHdf5Matcher? MatchHdf5 { get; init; }

    /// <summary>
    /// Optional recognizer from the file's leading bytes and extension, for a
    /// format with a signature of its own (GRIB2's <c>GRIB</c>, NetCDF-3's
    /// <c>CDF\x01</c>). <see cref="DatasetPipelineFactory"/> consults it for
    /// every file no envelope matcher claimed, including files whose extension
    /// says nothing (a NOMADS GRIB filter download), so a matcher must check the
    /// signature rather than trust the extension.
    /// </summary>
    public DatasetBytesMatcher? MatchBytes { get; init; }

    /// <summary>
    /// Optional recognizer for a folder that is one dataset: given a folder's
    /// path, decides whether the product reads the folder as a whole (a weather
    /// model run published one GRIB2 file per forecast hour, #1075). A
    /// product whose processors take a folder path sets it;
    /// <see cref="DatasetPipelineFactory"/> offers every folder to it, and
    /// <see cref="CreateFromPath"/> and <see cref="ReadMetadata"/> then get the
    /// folder's path. <see langword="null"/> for every other product.
    /// </summary>
    public Func<string, bool>? MatchFolder { get; init; }

    /// <summary>
    /// Returns the default plane for <paramref name="layerKind"/> from
    /// <see cref="DefaultPlanes"/>: its own entry, else the
    /// <see cref="AnyLayerKind"/> entry, else <see langword="null"/>.
    /// </summary>
    /// <param name="layerKind">The layer kind, or <see langword="null"/> for none.</param>
    public S98DisplayPlane? DefaultPlaneFor(string? layerKind)
    {
        var kind = layerKind?.Trim();
        if (!string.IsNullOrEmpty(kind) && TryGetPlane(kind, out var plane))
            return plane;
        return TryGetPlane(AnyLayerKind, out plane) ? plane : null;
    }

    private bool TryGetPlane(string kind, out S98DisplayPlane plane)
    {
        if (DefaultPlanes.TryGetValue(kind, out plane))
            return true;

        // A registration may build its table with the default comparer, so fall
        // back to a case-insensitive scan rather than miss "Area" for "area".
        foreach (var (key, value) in DefaultPlanes)
        {
            if (string.Equals(key, kind, StringComparison.OrdinalIgnoreCase))
            {
                plane = value;
                return true;
            }
        }

        plane = default;
        return false;
    }
}
