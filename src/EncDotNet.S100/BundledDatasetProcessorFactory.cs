using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100;

/// <summary>
/// A ready-to-use <see cref="IDatasetProcessorFactory"/> seeded with the
/// official S-100 feature and portrayal catalogues bundled in
/// <c>EncDotNet.S100.Specifications</c> and supporting every built-in product.
/// </summary>
/// <remarks>
/// <para>
/// Hand it to <c>S100MapsuiOptions.DatasetPipelineFactory</c> (or any other
/// <see cref="IDatasetProcessorFactory"/> consumer) so a host can load datasets
/// from a path without hand-wiring the portrayal / feature catalogue managers,
/// the Lua engine, the CRS transform factory, and the product registry. This is
/// the one-call replacement for the multi-step bootstrap a host would otherwise
/// copy from the viewer or CLI.
/// </para>
/// <para>
/// The factory owns long-lived catalogue managers whose parse caches survive for
/// its lifetime; <see cref="Dispose"/> releases them, so keep one instance alive
/// for as long as datasets are loaded and dispose it with (or before) the owning
/// session.
/// </para>
/// </remarks>
public sealed class BundledDatasetProcessorFactory : IDatasetProcessorFactory, IDisposable
{
    private readonly S100PipelineHost _host;
    private bool _disposed;

    private BundledDatasetProcessorFactory(S100PipelineHost host) => _host = host;

    /// <summary>
    /// Creates a factory seeded with the bundled official catalogues for every
    /// available product specification.
    /// </summary>
    public static BundledDatasetProcessorFactory Create() =>
        new(S100PipelineHost.Create());

    /// <inheritdoc />
    public IDatasetProcessor CreateProcessor(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host.Factory.CreateProcessor(path);
    }

    /// <summary>
    /// Creates a processor for the file at <paramref name="path"/>, honouring an
    /// optional caller-declared product specification
    /// (<paramref name="declaredProductSpec"/>) — e.g. a <c>--spec</c> hint or an
    /// exchange-set catalogue spec — instead of re-detecting it from the file.
    /// When the declared spec is null, blank, or unrecognised, detection from the
    /// file is used, identical to <see cref="CreateProcessor(string)"/>. Lets a
    /// host load a dataset whose product cannot be sniffed from its bytes but is
    /// known from an out-of-band source.
    /// </summary>
    public IDatasetProcessor CreateProcessor(string path, string? declaredProductSpec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(declaredProductSpec))
            return _host.Factory.CreateProcessor(path);

        var full = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(full)
            ?? throw new ArgumentException("Path must include a directory.", nameof(path));
        var source = Core.FileSystemAssetSource.Create(directory);
        return _host.CreateProcessor(source, System.IO.Path.GetFileName(full), declaredProductSpec);
    }

    /// <inheritdoc />
    public IDatasetProcessor CreateProcessorWithFilesystemUpdates(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host.Factory.CreateProcessorWithFilesystemUpdates(path);
    }

    /// <summary>Releases the bundled catalogue managers and their caches.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.Dispose();
    }
}
