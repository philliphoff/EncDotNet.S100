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
/// <see cref="Open(string)"/> is the batteries-included entry point: it detects
/// the product specification from the file and (lazily, on first access to a
/// data property) parses the dataset against the catalogues bundled in
/// <c>EncDotNet.S100.Specifications</c>. Advanced users who need a caller-supplied
/// catalogue can construct the per-spec reader directly instead.
/// </para>
/// </remarks>
public sealed class S100Dataset : IDisposable
{
    private readonly string _detectedSpec;
    private S100PipelineHost? _host;
    private IDatasetProcessor? _processor;
    private bool _disposed;

    private S100Dataset(string path, string detectedSpec)
    {
        Path = path;
        _detectedSpec = detectedSpec;
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
                $"Could not detect an S-100 product specification for: {System.IO.Path.GetFileName(path)}");

        return new S100Dataset(path, spec);
    }

    /// <summary>The path of the dataset file this instance was opened from.</summary>
    internal string Path { get; }

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
                _processor = _host.CreateProcessor(Path);
            }

            return _processor;
        }
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
