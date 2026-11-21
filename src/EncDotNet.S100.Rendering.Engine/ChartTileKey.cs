namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// Identifies one chart tile: the single raster of a tile slot that holds the
/// base plane of every cell touching the slot, drawn in stack order with
/// finer-cell coverage clipping applied (map rendering slice&#160;8).
/// </summary>
/// <param name="Band">The tile grid band (see <c>TileGrid</c>): a power-of-two resolution.</param>
/// <param name="X">The tile column in the band, increasing east.</param>
/// <param name="Y">The tile row in the band, increasing south.</param>
/// <param name="ScaleClass">
/// The SCAMIN scale class of the slot at the live scale: the thresholds are the
/// union of the slot's ops' scale limits inside the band's √2 window.
/// </param>
/// <param name="PixelSize">
/// The stored tile's edge in device pixels (core plus apron), so a tile drawn
/// at one device scale is never shown at another.
/// </param>
/// <param name="StackSlice">
/// A 128-bit hash of what is in the slot: the ordered scene keys of the
/// contributing stack layers (with any S-98 suppression and their opacity) and
/// the finer coverages that clip each. A slot changes key only when its own
/// content changes, so a landing cell re-rasters only the slots it touches,
/// and the key is content-addressed across sessions when every scene key is
/// stable. Zero for a layer bound to a single scene.
/// </param>
public readonly record struct ChartTileKey(
    int Band,
    int X,
    int Y,
    int ScaleClass,
    int PixelSize,
    UInt128 StackSlice);
