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

    private static void AssertIsPng(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > PngSignature.Length, "Rendered output is too small to be a PNG.");
        Assert.True(bytes.Take(PngSignature.Length).SequenceEqual(PngSignature),
            "Rendered output does not start with the PNG signature.");
    }
}
