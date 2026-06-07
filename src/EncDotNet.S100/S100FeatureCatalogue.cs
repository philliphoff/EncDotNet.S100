using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100;

/// <summary>
/// A feature catalogue (ISO 19110 / S-100 Part 5) — the lens through which an
/// <see cref="S100Dataset"/>'s features are read. Because decoding a feature's
/// type name and attributes presupposes a feature catalogue, feature access
/// lives here rather than on <see cref="S100Dataset"/>.
/// </summary>
/// <remarks>
/// Use <see cref="Bundled(string)"/> for the official catalogue shipped in
/// <c>EncDotNet.S100.Specifications</c>, or <see cref="FromStream(Stream)"/> to
/// supply your own.
/// </remarks>
public sealed class S100FeatureCatalogue : IDisposable
{
    private readonly byte[]? _customXml;
    private S100PipelineHost? _host;
    private bool _disposed;

    private S100FeatureCatalogue(byte[]? customXml) => _customXml = customXml;

    /// <summary>Whether this is the bundled catalogue for its product specification.</summary>
    internal bool IsBundled => _customXml is null;

    /// <summary>
    /// The official feature catalogue bundled in
    /// <c>EncDotNet.S100.Specifications</c> for the given product specification.
    /// </summary>
    /// <param name="productSpec">Product specification name (e.g. <c>"S-101"</c>).</param>
    public static S100FeatureCatalogue Bundled(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        return new S100FeatureCatalogue(customXml: null);
    }

    /// <summary>
    /// A caller-supplied feature catalogue read from <paramref name="featureCatalogueXml"/>.
    /// The stream is buffered, so the caller may dispose it immediately after this call.
    /// </summary>
    public static S100FeatureCatalogue FromStream(Stream featureCatalogueXml)
    {
        ArgumentNullException.ThrowIfNull(featureCatalogueXml);
        using var buffer = new MemoryStream();
        featureCatalogueXml.CopyTo(buffer);
        return new S100FeatureCatalogue(buffer.ToArray());
    }

    /// <summary>
    /// Opens a fresh read-only stream over the custom catalogue XML, or <c>null</c>
    /// for the bundled catalogue (which is resolved from the specifications package).
    /// </summary>
    internal Stream? OpenStream() =>
        _customXml is null ? null : new MemoryStream(_customXml, writable: false);

    /// <summary>
    /// Enumerates a lightweight summary of every feature in <paramref name="dataset"/>,
    /// with type names resolved through this catalogue.
    /// </summary>
    /// <param name="dataset">The dataset to read features from.</param>
    /// <returns>One <see cref="FeatureSummary"/> per feature; empty for coverage products.</returns>
    public IReadOnlyList<FeatureSummary> EnumerateFeatures(S100Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Read(dataset, processor => new List<FeatureSummary>(processor.EnumerateFeatures()));
    }

    /// <summary>
    /// Returns information about the feature identified by <paramref name="featureRef"/>,
    /// decoded against this catalogue, or <c>null</c> if it cannot be found.
    /// </summary>
    /// <param name="dataset">The dataset to read the feature from.</param>
    /// <param name="featureRef">The feature reference string (dataset-specific id).</param>
    public FeatureInfo? GetFeature(S100Dataset dataset, string featureRef)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrEmpty(featureRef);
        return Read(dataset, processor => processor.GetFeatureInfo(featureRef));
    }

    private TResult Read<TResult>(S100Dataset dataset, Func<IDatasetProcessor, TResult> read)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The bundled catalogue for the dataset's own spec is exactly what the
        // dataset already parsed, so reuse its processor and skip a second parse.
        if (IsBundled)
            return read(dataset.Processor);

        _host ??= S100PipelineHost.Create(dataset.SpecName, featureOverride: this);
        var processor = _host.CreateProcessor(dataset.Path);
        try
        {
            return read(processor);
        }
        finally
        {
            (processor as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Releases the catalogue parse cache held for a caller-supplied catalogue.
    /// A no-op for the bundled catalogue (which holds no per-instance resources).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host?.Dispose();
        _host = null;
    }
}
