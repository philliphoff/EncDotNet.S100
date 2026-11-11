namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Memoizes the (palette-independent) result of the pattern priority-clip
/// (<see cref="PatternPriorityClipper.Clip"/>) so a <see cref="VectorSceneBuilder"/>
/// re-build that does not change the clip inputs — most importantly a
/// Day/Dusk/Night palette switch — reuses the previously computed clip geometry
/// instead of repeating the expensive NetTopologySuite overlay.
/// </summary>
/// <param name="compute">
/// Runs the clip on a cache miss. The delegate invokes it only when it has no
/// cached result to return; on a hit it returns the cached geometry and
/// <paramref name="compute"/> is not called.
/// </param>
/// <returns>The clipped pattern geometry, cached or freshly computed.</returns>
/// <remarks>
/// The clipped boundary geometry is fully determined by the fixed dataset
/// geometry plus the mariner/ECDIS display state and is independent of the
/// palette (which only recolours the pattern tiles applied <em>after</em>
/// clipping), so a single cached result is valid across palette switches. The
/// Mapsui renderer wires this to its <c>IPatternClipCache</c> (bound to the
/// current portrayal cache key) for the default TiledScene ("B") arm — bringing
/// it to parity with the Mapsui feature ("A") arm. The headless Skia path leaves
/// it unset, since a headless render performs the clip exactly once.
/// </remarks>
public delegate IReadOnlyList<PatternPriorityClipper.ClippedPattern> PatternClipMemoizer(
    Func<IReadOnlyList<PatternPriorityClipper.ClippedPattern>> compute);
