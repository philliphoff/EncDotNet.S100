using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Sets the checked route's departure from a departure band: the Clearance
/// tab's (#903, D5) or the Timeline's window lane (#904, E2). A drag is one
/// undo step; each move snaps to a swept departure and recomputes live.
/// </summary>
internal sealed class DepartureDrag
{
    private readonly PassageCheckService _check;
    private IDisposable? _gesture;

    public DepartureDrag(PassageCheckService check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _check = check;
    }

    /// <summary>Whether a drag is open.</summary>
    public bool IsDragging => _gesture is not null;

    /// <summary>Opens a drag and moves the departure to <paramref name="departure"/>.</summary>
    public void Begin(DepartureBand band, DateTimeOffset departure)
    {
        _gesture?.Dispose();
        _gesture = _check.BeginDepartureGesture();
        MoveTo(band, departure);
    }

    /// <summary>Moves the departure to the swept departure nearest <paramref name="departure"/>.</summary>
    public void MoveTo(DepartureBand band, DateTimeOffset departure) => Set(band.DepartureAt(band.Fraction(departure)));

    /// <summary>Closes the drag, recording its undo step.</summary>
    public void End()
    {
        _gesture?.Dispose();
        _gesture = null;
    }

    /// <summary>Moves the departure by a number of swept departures.</summary>
    public void Nudge(DepartureBand band, int steps)
    {
        if (_check.Departure is { } departure)
            Set(band.Nudge(departure, steps));
    }

    private void Set(DateTimeOffset departure)
    {
        if (_check.Departure == departure && _check.HasRouteDeparture)
            return;
        _check.SetDeparture(departure);
    }
}
