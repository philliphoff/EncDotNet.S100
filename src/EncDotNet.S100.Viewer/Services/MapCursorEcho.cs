using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Where the pointer is, and over which map, for the cursor echo the other
/// panes of a split draw (split map design note D16, #1733).
/// </summary>
internal sealed class MapCursorEcho
{
    /// <summary>The map under the pointer, or <see langword="null"/> when it is over none.</summary>
    public MapViewId? Source { get; private set; }

    /// <summary>The pointer's position over <see cref="Source"/>, or <see langword="null"/>.</summary>
    public GeoPosition? Position { get; private set; }

    /// <summary>Raised when <see cref="Source"/> or <see cref="Position"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The pointer is at <paramref name="position"/> over
    /// <paramref name="source"/>, or (with <see langword="null"/>) has left
    /// it. Leaving a map that isn't the source changes nothing: the pointer
    /// can enter the next map before the last one hears it left.
    /// </summary>
    /// <param name="source">The map reporting the pointer.</param>
    /// <param name="position">Where the pointer is, or <see langword="null"/> when it left.</param>
    public void Report(MapViewId source, GeoPosition? position)
    {
        if (position is null)
        {
            if (Source != source)
                return;
            Source = null;
            Position = null;
        }
        else
        {
            if (Source == source && Position == position)
                return;
            Source = source;
            Position = position;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
