using EncDotNet.S100.Core.Caching;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// Process-wide state of the chart rendering engine. Until a chart session
/// owns its options (map rendering slice&#160;8), every map in a process
/// renders with one <see cref="ChartSessionOptions"/> instance.
/// </summary>
public static class ChartEngine
{
    /// <summary>
    /// The options the engine renders with, seeded from the environment on
    /// first use (<see cref="ChartSessionOptions.FromEnvironment"/>). A host
    /// changes them in place, for example from its settings.
    /// </summary>
    public static ChartSessionOptions Options { get; } = ChartSessionOptions.FromEnvironment();

    /// <summary>
    /// The host's content store, or <see langword="null"/>. When set before the
    /// first tile is drawn, the warm tile cache lives in its
    /// <see cref="TileKindName"/> kind (opened with a share of
    /// <see cref="ChartSessionOptions.TileDiskMb"/> unless the host opened it
    /// already), beside the host's other caches and under one budget. When
    /// unset, the engine creates its own store at
    /// <see cref="S100VectorTileRenderer.ResolveTileDiskDirectory"/>.
    /// </summary>
    public static ContentStore? ContentStore { get; set; }

    /// <summary>The name of the warm tile kind in a <see cref="Core.Caching.ContentStore"/> (also its folder name).</summary>
    public const string TileKindName = TileDiskCache.KindName;

    /// <summary>
    /// The options a host opens the warm tile kind with, when it opens the
    /// kind itself: versioned by the tile format, so a format change ignores
    /// and then deletes every older tile.
    /// </summary>
    public static ContentKindOptions TileKindOptions => TileDiskCache.KindOptions;
}
