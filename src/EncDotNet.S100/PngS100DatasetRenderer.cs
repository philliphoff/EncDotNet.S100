using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
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
public sealed class PngS100DatasetRenderer : IS100DatasetRenderer<byte[]>, IS100CompositeRenderer<byte[]>, IDisposable
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

    /// <inheritdoc />
    public Task<byte[]> RenderAsync(
        IReadOnlyList<S100Layer> layers,
        S100CompositeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            throw new ArgumentException("At least one layer is required to composite.", nameof(layers));
        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            ArgumentNullException.ThrowIfNull(layer.Dataset);
        }

        options ??= new S100CompositeOptions();
        return RenderCompositeCoreAsync(layers, options, cancellationToken);
    }

    private async Task<byte[]> RenderCompositeCoreAsync(
        IReadOnlyList<S100Layer> layers,
        S100CompositeOptions options,
        CancellationToken cancellationToken)
    {
        var mariner = options.Mariner ?? MarinerSettings.Default;

        // Build each layer's Mapsui-free portrayal result. Processors (and any
        // per-layer override host) are disposed once every result is captured;
        // the results are immutable snapshots safe to composite afterwards.
        var inputs = new List<HeadlessCompositeInput>(layers.Count);
        var toDispose = new List<IDisposable>(layers.Count * 2);
        try
        {
            foreach (var layer in layers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var host = ResolveHost(layer, out bool disposeHost);
                if (disposeHost)
                    toDispose.Add(host);

                var processor = host.CreateProcessor(layer.Dataset.Path);
                if (processor is IDisposable disposableProcessor)
                    toDispose.Add(disposableProcessor);

                var context = BuildCompositeContext(processor, options, mariner);

                switch (processor)
                {
                    case IVectorPortrayalSource vectorSource:
                    {
                        var result = await vectorSource
                            .BuildVectorPortrayalAsync(context, cancellationToken)
                            .ConfigureAwait(false);
                        inputs.Add(HeadlessCompositeInput.ForVector(result));
                        break;
                    }

                    case ICoveragePortrayalSource coverageSource:
                    {
                        var result = await coverageSource
                            .BuildCoveragePortrayalAsync(context, cancellationToken)
                            .ConfigureAwait(false);
                        inputs.Add(HeadlessCompositeInput.ForCoverage(result));
                        break;
                    }

                    default:
                        throw new NotSupportedException(
                            $"Headless compositing is not supported for {processor.Spec.Name}: "
                            + "the processor implements neither IVectorPortrayalSource nor "
                            + "ICoveragePortrayalSource.");
                }
            }

            var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
            var compositeOptions = new HeadlessCompositeOptions
            {
                Width = options.Width,
                Height = options.Height,
                Background = options.Background ?? new RgbaColor(255, 255, 255, 255),
                Viewport = options.Viewport,
                Mariner = mariner,
                HiddenCategories = options.HiddenCategories,
                Basemap = options.Basemap,
            };

            using var bitmap = compositor.Render(inputs, compositeOptions);
            return EncodePng(bitmap);
        }
        finally
        {
            for (int i = toDispose.Count - 1; i >= 0; i--)
                toDispose[i].Dispose();
        }
    }

    /// <summary>
    /// Resolves the pipeline host for a layer: a per-layer override host when the
    /// layer supplies a non-bundled feature or custom portrayal catalogue,
    /// otherwise the shared cached bundled host (reusing warmed parse caches).
    /// </summary>
    private S100PipelineHost ResolveHost(S100Layer layer, out bool disposeHost)
    {
        bool usesOverrides =
            layer.FeatureCatalogue is { IsBundled: false }
            || layer.PortrayalCatalogue is { CustomSource: not null };
        if (usesOverrides)
        {
            disposeHost = true;
            return S100PipelineHost.Create(
                layer.Dataset.SpecName,
                featureOverride: layer.FeatureCatalogue is { IsBundled: false } fc ? fc : null,
                portrayalOverride: layer.PortrayalCatalogue is { CustomSource: not null } pc ? pc : null);
        }

        disposeHost = false;
        return _bundledHost ??= S100PipelineHost.Create();
    }

    private static RenderContext BuildCompositeContext(
        IDatasetProcessor processor,
        S100CompositeOptions options,
        MarinerSettings mariner)
    {
        var rendererOptions = new S100RendererOptions
        {
            Width = options.Width,
            Height = options.Height,
            Palette = options.Palette,
            SymbolScale = options.SymbolScale,
            TextScale = options.TextScale,
            TimeStep = options.TimeStep,
            Background = options.Background,
            HiddenCategories = options.HiddenCategories,
        };
        return FacadeRenderContextBuilder.Build(processor, rendererOptions) with { Mariner = mariner };
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
