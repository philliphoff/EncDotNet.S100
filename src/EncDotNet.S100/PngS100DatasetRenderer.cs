using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using SkiaSharp;

namespace EncDotNet.S100;

/// <summary>
/// Renders an S-100 layer to PNG image bytes. This is the batteries-included
/// renderer: with the bundled catalogues the simple path is a single call —
/// <c>await new PngS100DatasetRenderer().RenderAsync(dataset)</c>.
/// </summary>
/// <remarks>
/// A single instance may be reused to render many datasets and layers; it caches
/// the bundled pipeline host so repeated renders reuse warmed catalogue parse
/// caches. The renderer is thread-safe for sequential reuse; concurrent calls on
/// one instance are not supported. Dispose to release the cached host.
/// </remarks>
public sealed class PngS100DatasetRenderer : IS100DatasetRenderer<byte[]>, IDisposable
{
    private S100PipelineHost? _bundledHost;
    private bool _disposed;

    /// <inheritdoc />
    public Task<byte[]> RenderAsync(
        S100Layer layer,
        S100RendererOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(layer.Dataset);

        options ??= new S100RendererOptions();
        return RenderCoreAsync(layer, options, cancellationToken);
    }

    private async Task<byte[]> RenderCoreAsync(
        S100Layer layer,
        S100RendererOptions options,
        CancellationToken cancellationToken)
    {
        var dataset = layer.Dataset;

        // Only a caller-supplied (non-bundled) catalogue is an override. An
        // explicit Bundled(...) catalogue is equivalent to leaving it null, so it
        // still reuses the cached bundled host and its warmed parse caches.
        bool usesOverrides =
            layer.FeatureCatalogue is { IsBundled: false }
            || layer.PortrayalCatalogue is { CustomSource: not null };
        S100PipelineHost host;
        bool disposeHost;
        if (usesOverrides)
        {
            host = S100PipelineHost.Create(
                dataset.SpecName,
                featureOverride: layer.FeatureCatalogue is { IsBundled: false } fc ? fc : null,
                portrayalOverride: layer.PortrayalCatalogue is { CustomSource: not null } pc ? pc : null);
            disposeHost = true;
        }
        else
        {
            host = _bundledHost ??= S100PipelineHost.Create();
            disposeHost = false;
        }

        IDatasetProcessor? processor = null;
        try
        {
            processor = host.CreateProcessor(dataset.Path);

            if (processor is not IHeadlessImageRenderer headless)
            {
                throw new NotSupportedException(
                    $"Headless image rendering is not supported for {processor.Spec.Name}.");
            }

            var context = FacadeRenderContextBuilder.Build(processor, options);

            using var bitmap = await headless
                .RenderHeadlessAsync(options.Width, options.Height, context, options.Background, cancellationToken)
                .ConfigureAwait(false);

            return EncodePng(bitmap);
        }
        finally
        {
            (processor as IDisposable)?.Dispose();
            if (disposeHost)
                host.Dispose();
        }
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Releases the cached bundled pipeline host.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _bundledHost?.Dispose();
        _bundledHost = null;
    }
}
