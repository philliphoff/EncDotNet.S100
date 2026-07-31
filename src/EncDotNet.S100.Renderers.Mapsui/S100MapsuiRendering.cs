namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Initializes the custom Mapsui style and layer renderers required to portray
/// S-100 map layers.
/// </summary>
public static class S100MapsuiRendering
{
    private static readonly object Sync = new();
    private static bool _registered;

    /// <summary>
    /// Registers all S-100 renderers with Mapsui in dependency order.
    /// The operation is idempotent and safe to call more than once.
    /// </summary>
    /// <remarks>
    /// Call this during application startup, before installing diagnostics that
    /// wrap Mapsui's renderer registry. Individual render paths retain their
    /// defensive registrations so one-shot consumers remain safe.
    /// </remarks>
    public static void Register()
    {
        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            CachedVectorStyleRenderer.Register();
            AnchoredPatternFillRenderer.Register();
            OverscaleCurtainRenderer.Register();

            S100VectorSnapshotRenderer.Register();
            S100VectorSceneRenderer.Register();
            S100VectorTileRenderer.Register();

            _registered = true;
        }
    }
}
