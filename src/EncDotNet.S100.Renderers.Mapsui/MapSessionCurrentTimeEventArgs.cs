namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Carries the new global map clock value after a
/// <see cref="MapsuiMapSession"/> clock change.
/// </summary>
public sealed class MapSessionCurrentTimeEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="currentTime">The clamped global clock value.</param>
    public MapSessionCurrentTimeEventArgs(DateTime currentTime)
    {
        CurrentTime = currentTime;
    }

    /// <summary>The clamped global clock value.</summary>
    public DateTime CurrentTime { get; }
}
