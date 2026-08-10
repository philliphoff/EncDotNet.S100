using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Tests;

/// <summary>
/// Exercises the batteries-included facade end-to-end against committed synthetic
/// GML fixtures: open → identify, enumerate/get features through the bundled
/// feature catalogue, and render to PNG through the bundled portrayal catalogue.
/// </summary>
public sealed class FacadeTests
{
    private static readonly string S124Surface =
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_surface.gml");

    private static readonly string S125Point =
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [SkippableFact]
    public void Open_DetectsSpec_AndReportsHeadlessCapability()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var ds = S100Dataset.Open(S124Surface);

        Assert.Equal("S-124", ds.Spec.Name);
        Assert.True(ds.CanRenderHeadless);
        Assert.Empty(ds.AvailableTimes);
    }

    [Fact]
    public void Open_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => S100Dataset.Open("does-not-exist.gml"));
    }

    [SkippableFact]
    public void BundledFeatureCatalogue_EnumeratesFeatures_AndResolvesOne()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var ds = S100Dataset.Open(S124Surface);
        var fc = S100FeatureCatalogue.Bundled(ds.Spec.Name);

        var features = fc.EnumerateFeatures(ds);
        Assert.NotEmpty(features);

        var first = features[0];
        var info = fc.GetFeature(ds, first.FeatureRef);
        Assert.NotNull(info);
        Assert.Equal(first.FeatureRef, info!.FeatureRef);
    }

    [SkippableFact]
    public async Task PngRenderer_BundledDefaults_ProducesPngBytes()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var ds = S100Dataset.Open(S124Surface);
        using var renderer = new PngS100DatasetRenderer();

        byte[] png = await renderer.RenderAsync(ds);

        AssertIsPng(png);
    }

    [SkippableFact]
    public async Task PngRenderer_ExplicitLayerWithBundledCatalogues_ProducesPng()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var ds = S100Dataset.Open(S124Surface);
        using var renderer = new PngS100DatasetRenderer();

        var layer = new S100Layer
        {
            Dataset = ds,
            FeatureCatalogue = S100FeatureCatalogue.Bundled(ds.Spec.Name),
            PortrayalCatalogue = S100PortrayalCatalogue.Bundled(ds.Spec.Name),
        };

        byte[] png = await renderer.RenderAsync(layer, new S100RendererOptions { Width = 256, Height = 256 });

        AssertIsPng(png);
    }

    [SkippableFact]
    public async Task PngRenderer_ReusedAcrossDatasets_ProducesPngForEach()
    {
        Skip.IfNot(File.Exists(S124Surface) && File.Exists(S125Point),
            "S-124 and S-125 fixtures not both present.");

        using var renderer = new PngS100DatasetRenderer();

        using (var a = S100Dataset.Open(S124Surface))
            AssertIsPng(await renderer.RenderAsync(a, new S100RendererOptions { Width = 256, Height = 256 }));

        using (var b = S100Dataset.Open(S125Point))
            AssertIsPng(await renderer.RenderAsync(b, new S100RendererOptions { Width = 256, Height = 256 }));
    }

    [SkippableFact]
    public async Task PngRenderer_CustomPortrayalCatalogue_IsReusableAcrossRenders()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        // A caller-supplied portrayal catalogue must survive being used by a
        // transient host (whose teardown must NOT dispose the caller's source),
        // so the same instance can drive a second render.
        var pc = S100PortrayalCatalogue.FromAssetSource(
            Specification.CreatePortrayalCatalogueSource("S-124"));

        using var renderer = new PngS100DatasetRenderer();
        var options = new S100RendererOptions { Width = 256, Height = 256 };

        using (var ds = S100Dataset.Open(S124Surface))
        {
            var layer = new S100Layer { Dataset = ds, PortrayalCatalogue = pc };
            AssertIsPng(await renderer.RenderAsync(layer, options));
            AssertIsPng(await renderer.RenderAsync(layer, options));
        }
    }

    [SkippableFact]
    public async Task PngRenderer_CompositesMultipleVectorLayers_ProducesPng()
    {
        Skip.IfNot(File.Exists(S124Surface) && File.Exists(S125Point),
            "S-124 and S-125 fixtures not both present.");

        using var a = S100Dataset.Open(S124Surface);
        using var b = S100Dataset.Open(S125Point);
        using var renderer = new PngS100DatasetRenderer();

        var layers = new[]
        {
            new S100Layer { Dataset = a },
            new S100Layer { Dataset = b },
        };

        byte[] png = await renderer.RenderAsync(
            layers,
            new S100CompositeOptions { Width = 256, Height = 256 });

        AssertIsPng(png);
    }

    [Fact]
    public async Task PngRenderer_Composite_EmptyLayerList_Throws()
    {
        using var renderer = new PngS100DatasetRenderer();

        await Assert.ThrowsAsync<ArgumentException>(
            () => renderer.RenderAsync(Array.Empty<S100Layer>(), new S100CompositeOptions()));
    }

    [SkippableFact]
    public void PngRenderer_Composite_PropagatesExplicitViewportToRenderContext()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var dataset = S100Dataset.Open(S124Surface);
        var viewport = new Pipelines.Viewport
        {
            MinLongitude = -80,
            MaxLongitude = -60,
            MinLatitude = 30,
            MaxLatitude = 45,
            WidthPixels = 400,
            HeightPixels = 300,
            ScaleDenominator = 20_000_000,
        };
        var options = new S100CompositeOptions { Viewport = viewport };

        var context = PngS100DatasetRenderer.BuildCompositeContext(
            dataset.Processor,
            options,
            Pipelines.MarinerSettings.Default);

        Assert.Same(viewport, context.Viewport);
    }

    [SkippableFact]
    public void PngRenderer_Composite_PropagatesEcdisDisplayToRenderContext()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var dataset = S100Dataset.Open(S124Surface);
        var ecdis = new Datasets.Pipelines.EcdisDisplaySettings
        {
            Category = Datasets.Pipelines.EcdisDisplayCategory.DisplayBase,
        };
        var options = new S100CompositeOptions { EcdisDisplay = ecdis };

        var context = PngS100DatasetRenderer.BuildCompositeContext(
            dataset.Processor,
            options,
            Pipelines.MarinerSettings.Default);

        // The composite path must carry the full ECDIS snapshot onto each layer's
        // render context (issue #567); before the fix this was always null and
        // every processor fell back to its unfiltered "All" default.
        Assert.Same(ecdis, context.EcdisDisplay);
    }

    [SkippableFact]
    public void PngRenderer_Composite_PerSpecDisplayMode_OverridesGlobalDisplayModeId()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var dataset = S100Dataset.Open(S124Surface);
        var spec = dataset.Processor.PortrayalSpec.Name; // "S-124"
        var ecdis = new Datasets.Pipelines.EcdisDisplaySettings
        {
            ActiveDisplayModes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [spec] = "PerSpecMode",
            },
        };
        var options = new S100CompositeOptions
        {
            DisplayModeId = "GlobalMode",
            EcdisDisplay = ecdis,
        };

        var context = PngS100DatasetRenderer.BuildCompositeContext(
            dataset.Processor,
            options,
            Pipelines.MarinerSettings.Default);

        // A per-spec ActiveDisplayModes entry wins over the global DisplayModeId,
        // matching MapPresentationState.ApplyTo's projection semantics.
        Assert.Equal("PerSpecMode", context.DisplayModeId);
    }

    [SkippableFact]
    public void PngRenderer_Composite_GlobalDisplayModeId_UsedWhenNoPerSpecEntry()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var dataset = S100Dataset.Open(S124Surface);
        var ecdis = new Datasets.Pipelines.EcdisDisplaySettings
        {
            // An entry for a different spec must not affect this layer.
            ActiveDisplayModes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["S-411"] = "OtherSpecMode",
            },
        };
        var options = new S100CompositeOptions
        {
            DisplayModeId = "GlobalMode",
            EcdisDisplay = ecdis,
        };

        var context = PngS100DatasetRenderer.BuildCompositeContext(
            dataset.Processor,
            options,
            Pipelines.MarinerSettings.Default);

        Assert.Equal("GlobalMode", context.DisplayModeId);
    }

    [SkippableFact]
    public async Task PngRenderer_Composite_HiddenCategories_ChangesOutput()
    {
        Skip.IfNot(File.Exists(S124Surface) && File.Exists(S125Point),
            "S-124 and S-125 fixtures not both present.");

        using var a = S100Dataset.Open(S124Surface);
        using var b = S100Dataset.Open(S125Point);
        using var renderer = new PngS100DatasetRenderer();

        var layers = new[]
        {
            new S100Layer { Dataset = a },
            new S100Layer { Dataset = b },
        };

        byte[] shown = await renderer.RenderAsync(
            layers, new S100CompositeOptions { Width = 256, Height = 256 });

        // Suppressing every drawing-instruction category must reach each layer's
        // pipeline in the composite (the option is applied globally), yielding a
        // different image than the fully-drawn composite.
        var allHidden = EncDotNet.S100.Pipelines.Vector.DrawingInstructionCategory.Areas
            | EncDotNet.S100.Pipelines.Vector.DrawingInstructionCategory.Lines
            | EncDotNet.S100.Pipelines.Vector.DrawingInstructionCategory.Points
            | EncDotNet.S100.Pipelines.Vector.DrawingInstructionCategory.Text;

        byte[] hidden = await renderer.RenderAsync(
            layers,
            new S100CompositeOptions { Width = 256, Height = 256, HiddenCategories = allHidden });

        AssertIsPng(shown);
        AssertIsPng(hidden);
        Assert.False(shown.SequenceEqual(hidden),
            "Hiding all categories should change the composited output.");
    }

    [SkippableFact]
    public async Task PngRenderer_CompositesResidentProcessors_ReusableWithoutReparse()
    {
        // Issue #566: a host that keeps resident processors can composite them
        // directly and repeatedly, with no per-render re-parse. One processor is
        // parsed once (S100Dataset.Processor) and rendered twice.
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var ds = S100Dataset.Open(S124Surface);
        using var renderer = new PngS100DatasetRenderer();

        var processors = new[] { ds.Processor };
        var options = new S100CompositeOptions { Width = 256, Height = 256 };

        AssertIsPng(await renderer.RenderAsync(processors, options));
        // Same resident processor again — must still render (no disposal on the
        // caller's processor by the renderer).
        AssertIsPng(await renderer.RenderAsync(processors, options));
    }

    [SkippableFact]
    public void ProjectFromProcessor_MatchesStreamProjection()
    {
        // Issue #566: projecting a LoadedDataset from a resident processor must
        // yield the same catalog entry as parsing the bytes afresh — same spec,
        // bounds, temporal coverage, and payload variant.
        Skip.IfNot(File.Exists(S124Surface) && File.Exists(S125Point),
            "S-124 and S-125 fixtures not both present.");

        foreach (var (spec, path) in new[] { ("S-124", S124Surface), ("S-125", S125Point) })
        {
            var id = new Datasets.Pipelines.Catalog.DatasetId("d");

            using var ds = S100Dataset.Open(path);
            var fromProcessor = Datasets.Pipelines.Catalog.LoadedDatasetProjector.Project(id, ds.Processor);

            using var stream = File.OpenRead(path);
            var fromStream = Datasets.Pipelines.Catalog.LoadedDatasetProjector.Project(id, spec, stream);

            Assert.NotNull(fromProcessor);
            Assert.NotNull(fromStream);
            Assert.Equal(fromStream!.Spec, fromProcessor!.Spec);
            Assert.Equal(fromStream.Bounds, fromProcessor.Bounds);
            Assert.Equal(fromStream.TimeRange, fromProcessor.TimeRange);
            Assert.Equal(fromStream.Data.GetType(), fromProcessor.Data.GetType());
        }
    }

    [SkippableFact]
    public void BundledFactory_DeclaredSpec_RescuesFileWhoseExtensionDefeatsDetection()
    {
        // Issue #566 review: a declared product spec (a --spec hint or an
        // exchange-set catalogue spec) must be honoured when building the resident
        // processor, so a dataset whose product cannot be sniffed from its bytes /
        // extension still loads. Detection keys off the extension, so an S-125
        // GML saved as ".xml" is undetectable — but the declared spec rescues it.
        Skip.IfNot(File.Exists(S125Point), "S-125 fixture not present.");

        var xml = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xml");
        File.Copy(S125Point, xml, overwrite: true);
        try
        {
            using var factory = BundledDatasetProcessorFactory.Create();

            // Detection alone cannot classify a ".xml" file.
            Assert.Throws<NotSupportedException>(() => factory.CreateProcessor(xml));

            // The declared spec forces the S-125 pipeline.
            var processor = factory.CreateProcessor(xml, "S-125");
            try
            {
                Assert.Equal("S-125", processor.Spec.Name);
            }
            finally
            {
                (processor as IDisposable)?.Dispose();
            }
        }
        finally { File.Delete(xml); }
    }

    private static void AssertIsPng(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > PngSignature.Length, "Rendered output is too small to be a PNG.");
        Assert.True(bytes.Take(PngSignature.Length).SequenceEqual(PngSignature),
            "Rendered output does not start with the PNG signature.");
    }
}
