using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100;

/// <summary>
/// An opened S-100 dataset — the raw data plus its detected product
/// specification. A dataset carries no rendering or feature-decoding behaviour:
/// rendering is performed by an <see cref="IS100DatasetRenderer{TResult}"/>, and
/// feature access (which presupposes a feature catalogue) lives on
/// <see cref="S100FeatureCatalogue"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open(string)"/> is the batteries-included entry point for a loose
/// file: it detects the product specification from the file and (lazily, on first
/// access to a data property) parses the dataset against the catalogues bundled in
/// <c>EncDotNet.S100.Specifications</c>. <see cref="OpenAsync"/> does the same for
/// a dataset inside an <see cref="IAssetSource"/> (a folder, a ZIP archive, or a
/// decorating source such as a caching or decrypting one), and
/// <see cref="S100ExchangeSet"/> opens the datasets an exchange set lists.
/// Advanced users who need a caller-supplied catalogue can construct the per-spec
/// reader directly instead.
/// </para>
/// </remarks>
public sealed class S100Dataset : IDisposable
{
    private readonly string _detectedSpec;
    private readonly Func<S100PipelineHost, IDatasetProcessor> _createProcessor;
    private S100PipelineHost? _host;
    private IDatasetProcessor? _processor;
    private bool _disposed;

    private S100Dataset(string detectedSpec, Func<S100PipelineHost, IDatasetProcessor> createProcessor)
    {
        _detectedSpec = detectedSpec;
        _createProcessor = createProcessor;
    }

    /// <summary>
    /// Opens the dataset file at <paramref name="path"/>, detecting its S-100
    /// product specification.
    /// </summary>
    /// <param name="path">Path to a loose S-100 dataset file (ISO 8211, HDF5, or GML).</param>
    /// <returns>An opened dataset.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="NotSupportedException">
    /// The file is not a recognised S-100 product specification.
    /// </exception>
    public static S100Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("S-100 dataset file not found.", path);

        var spec = DatasetPipelineFactory.DetectProductSpec(path)
            ?? throw new NotSupportedException(
                $"Could not detect an S-100 product specification for: {Path.GetFileName(path)}");

        return new S100Dataset(spec, host => host.CreateProcessor(path));
    }

    /// <summary>
    /// Opens the dataset at <paramref name="relativePath"/> inside
    /// <paramref name="source"/>, detecting its S-100 product specification from
    /// the dataset content (the ISO 8211 envelope, the HDF5
    /// <c>productSpecification</c> attribute, or the GML root element).
    /// </summary>
    /// <param name="source">
    /// The asset source holding the dataset: a folder
    /// (<see cref="FileSystemAssetSource"/>), a ZIP archive
    /// (<see cref="ZipAssetSource"/>), or a decorator over either. The dataset
    /// borrows the source: the caller keeps ownership and must keep it alive
    /// until the dataset is disposed, because the dataset is parsed lazily on
    /// first use.
    /// </param>
    /// <param name="relativePath">
    /// The dataset's path relative to <paramref name="source"/>
    /// (e.g. <c>"S-101/DATASET_FILES/101AA00DS0019.000"</c>).
    /// </param>
    /// <param name="cancellationToken">Cancels reading the dataset for detection.</param>
    /// <returns>An opened dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="source"/> has no file at <paramref name="relativePath"/>
    /// (the exact exception type is the source's, e.g.
    /// <see cref="DirectoryNotFoundException"/> for a missing folder).
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The dataset is not a recognised S-100 product specification.
    /// </exception>
    public static async Task<S100Dataset> OpenAsync(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var spec = await DatasetPipelineFactory
            .DetectProductSpecFromSourceAsync(source, relativePath, cancellationToken)
            .ConfigureAwait(false);
        if (spec is null)
        {
            // Detection reports an unreadable file the same as an unrecognised
            // one; open it once so a missing file surfaces as the source's own
            // not-found exception rather than as "unsupported".
            await using (await source.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false))
            {
            }

            throw new NotSupportedException(
                $"Could not detect an S-100 product specification for: {Path.GetFileName(relativePath)}");
        }

        return FromSource(source, relativePath, spec, supportFiles: null);
    }

    /// <summary>
    /// Creates a dataset for <paramref name="relativePath"/> inside
    /// <paramref name="source"/> whose product specification is already known
    /// (declared by an exchange-set catalogue or detected by the caller).
    /// </summary>
    internal static S100Dataset FromSource(
        IAssetSource source,
        string relativePath,
        string spec,
        IReadOnlyDictionary<string, string>? supportFiles) =>
        new(spec, host => host.PipelineFactory.CreateProcessor(source, relativePath, spec, supportFiles));

    /// <summary>
    /// Creates an S-101 dataset for the base cell at
    /// <paramref name="baseRelativePath"/> with the sequential updates at
    /// <paramref name="updateRelativePaths"/> applied, all read from
    /// <paramref name="source"/>.
    /// </summary>
    internal static S100Dataset FromS101CellWithUpdates(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        IReadOnlyDictionary<string, string>? supportFiles) =>
        new("S-101", host => host.PipelineFactory.CreateS101ProcessorWithUpdates(
            source, baseRelativePath, updateRelativePaths, supportFiles));

    /// <summary>
    /// The detected product specification name (e.g. <c>"S-101"</c>) without the
    /// edition, available without parsing the dataset.
    /// </summary>
    internal string SpecName => _detectedSpec;

    /// <summary>
    /// The product specification (name and edition) the dataset declares
    /// conformance to. The edition is read from the dataset itself.
    /// </summary>
    public SpecRef Spec => Processor.Spec;

    /// <summary>
    /// Whether this dataset can be rendered to a standalone image by the
    /// headless renderers (vector products and gridded coverages can; some
    /// shapes such as fixed-station time series cannot).
    /// </summary>
    public bool CanRenderHeadless => Processor is IHeadlessImageRenderer;

    /// <summary>
    /// The available time steps for time-aware products (S-104, S-111), in
    /// dataset order; empty for static products.
    /// </summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        Processor is ITimeAwareDatasetProcessor timeAware
            ? timeAware.AvailableTimes
            : Array.Empty<DateTime>();

    /// <summary>
    /// The processor parsed against the bundled catalogues, created lazily and
    /// reused for this dataset's data operations.
    /// </summary>
    internal IDatasetProcessor Processor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor is null)
            {
                _host = S100PipelineHost.Create();
                _processor = _createProcessor(_host);
            }

            return _processor;
        }
    }

    /// <summary>
    /// Creates a new processor for this dataset in <paramref name="host"/>, whose
    /// catalogues may differ from the bundled ones (a renderer or feature
    /// catalogue with overrides). The caller owns the returned processor.
    /// </summary>
    internal IDatasetProcessor CreateProcessor(S100PipelineHost host)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _createProcessor(host);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        (_processor as IDisposable)?.Dispose();
        _host?.Dispose();
        _processor = null;
        _host = null;
    }
}
