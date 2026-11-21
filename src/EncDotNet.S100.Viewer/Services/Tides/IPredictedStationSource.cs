using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// What a predicted tide or current station source tells its live feed's
/// Datasets row (#1630), beyond the features it draws.
/// </summary>
internal interface IPredictedStationSource : IPausableDynamicSource
{
    /// <summary>
    /// Whether the view is wider than the stations are drawn at (1:500,000,
    /// design D6); <see langword="false"/> while there's no view.
    /// </summary>
    bool IsViewTooWide { get; }

    /// <summary>Raised after the features were recomputed, on the thread that recomputed them.</summary>
    event EventHandler? Refreshed;
}
