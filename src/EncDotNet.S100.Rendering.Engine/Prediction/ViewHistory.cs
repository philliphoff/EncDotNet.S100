using System.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// What the view has done recently (map rendering slice&#160;9): its current
/// view, the motion the host reported, an EMA of the centre velocity and of
/// the zoom rate, the start view, and the places it rested at. Not
/// thread-safe; <see cref="DemandPlanner"/> guards it.
/// </summary>
public sealed class ViewHistory
{
    /// <summary>How long the view must stay put for the place to count as one it rested at.</summary>
    public static readonly TimeSpan RestTime = TimeSpan.FromMilliseconds(750);

    /// <summary>How many rested places are kept, most recent first.</summary>
    public const int MaxRecentPlaces = 4;

    /// <summary>
    /// After this long without a view change, the velocity and zoom-rate
    /// estimates no longer describe a gesture in progress.
    /// </summary>
    public static readonly TimeSpan MotionStaleAfter = TimeSpan.FromMilliseconds(250);

    private readonly List<ViewState> _recentPlaces = [];
    private ViewState? _restCandidate;
    private long _restCandidateSince;

    /// <summary>The latest view, or <see langword="null"/> before the first.</summary>
    public ViewState? Current { get; private set; }

    /// <summary>The <see cref="Stopwatch"/> timestamp of <see cref="Current"/>.</summary>
    public long CurrentTimestamp { get; private set; }

    /// <summary>The <see cref="Stopwatch"/> timestamp the view last changed at.</summary>
    public long LastChangeTimestamp { get; private set; }

    /// <summary>The first view the host reported.</summary>
    public ViewState? Start { get; private set; }

    /// <summary>The motion the host reported.</summary>
    public ViewMotion Motion { get; internal set; }

    /// <summary>EMA of the centre velocity, EPSG:3857 metres per second (east).</summary>
    public double VelocityX { get; private set; }

    /// <summary>EMA of the centre velocity, EPSG:3857 metres per second (north).</summary>
    public double VelocityY { get; private set; }

    /// <summary>
    /// EMA of the zoom rate: the change of the natural log of the resolution
    /// per second. Positive zooms out.
    /// </summary>
    public double ZoomRate { get; private set; }

    /// <summary>
    /// Places the view rested at for at least <see cref="RestTime"/>, most
    /// recent first, without the current view.
    /// </summary>
    public IReadOnlyList<ViewState> RecentPlaces => _recentPlaces;

    /// <summary>Whether the velocity and zoom-rate estimates describe a gesture in progress at <paramref name="timestamp"/>.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    /// <returns><see langword="true"/> when the view changed within <see cref="MotionStaleAfter"/>.</returns>
    public bool IsMoving(long timestamp) =>
        LastChangeTimestamp != 0
        && Stopwatch.GetElapsedTime(LastChangeTimestamp, timestamp) < MotionStaleAfter;

    /// <summary>Folds a view the host showed at <paramref name="timestamp"/> into the history.</summary>
    /// <param name="view">The view.</param>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    public void Add(ViewState view, long timestamp)
    {
        if (!view.HasSize || !(view.Resolution > 0))
        {
            return;
        }

        Start ??= view;
        if (Current is not { } previous)
        {
            Current = view;
            CurrentTimestamp = timestamp;
            _restCandidate = view;
            _restCandidateSince = timestamp;
            return;
        }

        if (previous == view)
        {
            CurrentTimestamp = timestamp;
            return;
        }

        var dt = Stopwatch.GetElapsedTime(CurrentTimestamp, timestamp).TotalSeconds;

        // A view held long enough is a place the user may come back to.
        if (_restCandidate is { } candidate
            && Stopwatch.GetElapsedTime(_restCandidateSince, timestamp) >= RestTime)
        {
            Remember(candidate);
        }

        // After a pause the old velocity says nothing about this gesture, and
        // the first step of a new one (or a jump) has no rate to measure.
        if (!IsMoving(timestamp))
        {
            VelocityX = 0;
            VelocityY = 0;
            ZoomRate = 0;
        }
        else if (dt > 0 && double.IsFinite(dt))
        {
            (VelocityX, VelocityY) = VelocityEstimator.Update(
                VelocityX, VelocityY,
                view.CenterX - previous.CenterX, view.CenterY - previous.CenterY,
                dt);
            var instant = Math.Log(view.Resolution / previous.Resolution) / dt;
            ZoomRate = (1 - VelocityEstimator.DefaultAlpha) * ZoomRate + VelocityEstimator.DefaultAlpha * instant;
        }

        Current = view;
        CurrentTimestamp = timestamp;
        LastChangeTimestamp = timestamp;
        _restCandidate = view;
        _restCandidateSince = timestamp;
    }

    /// <summary>
    /// Marks the current view as one the view rested at if it has been held
    /// for <see cref="RestTime"/> by <paramref name="timestamp"/>.
    /// </summary>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    internal void NoteIdle(long timestamp)
    {
        if (_restCandidate is { } candidate
            && Stopwatch.GetElapsedTime(_restCandidateSince, timestamp) >= RestTime)
        {
            Remember(candidate);
            _restCandidate = null;
        }
    }

    private void Remember(ViewState place)
    {
        // One entry per place: a view that overlaps a remembered one at a
        // similar scale replaces it.
        for (var i = _recentPlaces.Count - 1; i >= 0; i--)
        {
            if (SamePlace(_recentPlaces[i], place))
            {
                _recentPlaces.RemoveAt(i);
            }
        }

        _recentPlaces.Insert(0, place);
        if (_recentPlaces.Count > MaxRecentPlaces)
        {
            _recentPlaces.RemoveAt(_recentPlaces.Count - 1);
        }
    }

    /// <summary>
    /// Whether two views show about the same place: centres within half a
    /// view of each other, and resolutions within a factor of two.
    /// </summary>
    internal static bool SamePlace(ViewState a, ViewState b)
    {
        var ratio = a.Resolution / b.Resolution;
        if (ratio is > 2.0 or < 0.5)
        {
            return false;
        }

        var halfWidth = a.Width * a.Resolution * 0.5;
        var halfHeight = a.Height * a.Resolution * 0.5;
        return Math.Abs(a.CenterX - b.CenterX) <= halfWidth
            && Math.Abs(a.CenterY - b.CenterY) <= halfHeight;
    }
}
