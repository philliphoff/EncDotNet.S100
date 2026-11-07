using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Viewer.Services.Caching;

/// <summary>
/// The viewer's content stores (map rendering slice 11): one for the render
/// caches (warm tiles, portrayal display lists, pattern clips) and one for the
/// profile's derived data (dataset metadata, S-57 catalogues). They are the
/// same store unless <c>--cache-dir</c> moved the render caches elsewhere, so
/// normally every cache shares one byte budget.
/// </summary>
internal sealed class ViewerContentStores : IDisposable
{
    /// <summary>The portrayal display-list kind's share of the budget.</summary>
    internal const long PortrayalShareBytes = 1024L * 1024 * 1024;

    /// <summary>The pattern-clip kind's share of the budget.</summary>
    internal const long PatternClipShareBytes = 256L * 1024 * 1024;

    /// <summary>The dataset-metadata kind's share of the budget.</summary>
    internal const long MetadataShareBytes = 16L * 1024 * 1024;

    /// <summary>The S-57 catalogue kind's share of the budget.</summary>
    internal const long S57CatalogShareBytes = 16L * 1024 * 1024;

    public ViewerContentStores(ViewerDataPaths paths, long tileShareBytes)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Render = new ContentStore(paths.RenderStoreDirectory);
        Data = string.Equals(
                Path.GetFullPath(paths.RenderStoreDirectory),
                Path.GetFullPath(paths.CacheStoreDirectory),
                StringComparison.Ordinal)
            ? Render
            : new ContentStore(paths.CacheStoreDirectory);

        Tiles = Render.Open(ChartEngine.TileKindName, tileShareBytes, ChartEngine.TileKindOptions);
        Portrayal = Render.Open(
            DiskPortrayalInstructionCache.KindName, PortrayalShareBytes, DiskPortrayalInstructionCache.KindOptions);
        PatternClips = Render.Open(
            DiskPatternClipCache.KindName, PatternClipShareBytes, DiskPatternClipCache.KindOptions);
        Metadata = Data.Open(
            DiskDatasetMetadataCache.KindName, MetadataShareBytes, DiskDatasetMetadataCache.KindOptions);
        S57Catalogs = Data.Open(DiskS57CatalogCache.KindName, S57CatalogShareBytes, DiskS57CatalogCache.KindOptions);
    }

    /// <summary>The store of the render caches.</summary>
    public ContentStore Render { get; }

    /// <summary>The store of the profile's derived data (the render store unless <c>--cache-dir</c> is in use).</summary>
    public ContentStore Data { get; }

    /// <summary>The warm tile kind.</summary>
    public ContentKind Tiles { get; }

    /// <summary>The portrayal display-list kind.</summary>
    public ContentKind Portrayal { get; }

    /// <summary>The pattern-clip kind.</summary>
    public ContentKind PatternClips { get; }

    /// <summary>The dataset-metadata kind.</summary>
    public ContentKind Metadata { get; }

    /// <summary>The S-57 catalogue kind.</summary>
    public ContentKind S57Catalogs { get; }

    /// <summary>Lets queued writes finish briefly, then stops both stores.</summary>
    public void Dispose()
    {
        Render.Dispose();
        if (!ReferenceEquals(Data, Render))
        {
            Data.Dispose();
        }
    }
}
