using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The Range &amp; bearing tool's model (#1130 D11–D16): an origin, a range
/// and a bearing, either of which can be locked, the end they give along a
/// rhumb line or a great circle, and optional range rings. Free of Avalonia
/// so it's testable; <see cref="RangeBearingTool"/> feeds it pointer input,
/// the card and the MCP tools feed it typed values.
/// </summary>
/// <remarks>
/// <para>
/// The first click sets the origin; after that the cursor drives whatever
/// isn't locked (the bearing when the range is locked, like a VRM; the range
/// when the bearing is locked, like an EBL; both otherwise). A second click
/// fixes the end, and a click after that starts again from a new origin,
/// keeping the locks.
/// </para>
/// <para>
/// The state is application-scoped: the map tool, the card and the MCP tools
/// share one instance. <see cref="IsToolActive"/> is how the MCP tools turn
/// the map tool on, the way <c>RoutesService.IsEditModeActive</c> does for
/// the route editor.
/// </para>
/// </remarks>
internal sealed class RangeBearingState
{
    /// <summary>Default ring interval: one nautical mile.</summary>
    public const double DefaultRingIntervalMetres = PassageGeodesy.MetersPerNauticalMile;

    /// <summary>Default number of rings.</summary>
    public const int DefaultRingCount = 3;

    /// <summary>Most rings drawn, so a tiny interval can't flood the map.</summary>
    public const int MaxRingCount = 20;

    private readonly ViewerSettings? _settings;
    private LegGeometry _geometry;
    private bool _isToolActive;

    /// <param name="settings">Where the geometry persists; <c>null</c> keeps it for the session only.</param>
    public RangeBearingState(ViewerSettings? settings = null)
    {
        _settings = settings;
        _geometry = Enum.TryParse<LegGeometry>(settings?.RangeBearingGeometry, ignoreCase: true, out var g)
            ? g
            : LegGeometry.Rhumb;
    }

    /// <summary>Where the tool is in its click sequence.</summary>
    public enum RangeBearingPhase
    {
        /// <summary>No origin yet: the next click places it.</summary>
        PlacingOrigin,

        /// <summary>The origin is set and the end follows the cursor.</summary>
        Following,

        /// <summary>The end is fixed; the next click starts a new origin.</summary>
        Fixed,
    }

    /// <summary>Raised after anything the overlay or card shows changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="IsToolActive"/> changes.</summary>
    public event EventHandler? ToolActiveChanged;

    /// <summary>
    /// Whether the Range &amp; bearing map tool is on. The view model keeps the
    /// map tool controller and this flag in step.
    /// </summary>
    public bool IsToolActive
    {
        get => _isToolActive;
        set
        {
            if (_isToolActive == value)
                return;
            _isToolActive = value;
            ToolActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Rhumb line or great circle: how the bearing line runs and how the end is
    /// computed. The rings are geodesic circles either way. Remembered.
    /// </summary>
    public LegGeometry Geometry
    {
        get => _geometry;
        set
        {
            if (_geometry == value)
                return;
            _geometry = value;
            if (_settings is not null)
            {
                _settings.RangeBearingGeometry = value.ToString();
                try { _settings.Save(); } catch { /* best-effort */ }
            }

            // The cursor-driven value was measured along the old geometry.
            if (Phase == RangeBearingPhase.Following && _cursor is { } cursor)
                ApplyCursor(cursor);
            RaiseChanged();
        }
    }

    /// <summary>Where the click sequence is.</summary>
    public RangeBearingPhase Phase { get; private set; } = RangeBearingPhase.PlacingOrigin;

    /// <summary>The origin, or <c>null</c> before it's placed.</summary>
    public GeoPosition? Origin { get; private set; }

    /// <summary>The range in metres, typed or from the cursor; <c>null</c> when there's none yet.</summary>
    public double? RangeMetres { get; private set; }

    /// <summary>The true bearing in degrees [0, 360), typed or from the cursor; <c>null</c> when there's none yet.</summary>
    public double? BearingDegrees { get; private set; }

    /// <summary>Whether the range was typed, so the cursor only swings the bearing.</summary>
    public bool IsRangeLocked { get; private set; }

    /// <summary>Whether the bearing was typed, so the cursor only changes the range.</summary>
    public bool IsBearingLocked { get; private set; }

    /// <summary>Whether range rings are drawn.</summary>
    public bool RingsEnabled { get; private set; }

    /// <summary>Distance between rings in metres.</summary>
    public double RingIntervalMetres { get; private set; } = DefaultRingIntervalMetres;

    /// <summary>How many rings are drawn.</summary>
    public int RingCount { get; private set; } = DefaultRingCount;

    private GeoPosition? _cursor;

    /// <summary>
    /// The end: <see cref="RangeMetres"/> from <see cref="Origin"/> on
    /// <see cref="BearingDegrees"/> along <see cref="Geometry"/>; <c>null</c>
    /// until all three are known.
    /// </summary>
    public GeoPosition? End =>
        Origin is { } origin && RangeMetres is { } range && BearingDegrees is { } bearing
            ? MarineGeodesy.Destination(Geometry, origin, bearing, range)
            : null;

    /// <summary>The true bearing at <see cref="End"/> back to <see cref="Origin"/>, or <c>null</c> without an end.</summary>
    public double? ReverseBearingDegrees =>
        Origin is { } origin && End is { } end
            ? MarineGeodesy.ReverseBearingDegrees(Geometry, origin, end)
            : null;

    /// <summary>The ranges, in metres, of the rings drawn: none while rings are off.</summary>
    public IReadOnlyList<double> RingRanges
    {
        get
        {
            if (!RingsEnabled || RingCount <= 0 || RingIntervalMetres <= 0)
                return [];
            var ranges = new double[RingCount];
            for (var i = 0; i < ranges.Length; i++)
                ranges[i] = RingIntervalMetres * (i + 1);
            return ranges;
        }
    }

    /// <summary>
    /// The cursor moved to <paramref name="cursor"/> (<c>null</c> when it left
    /// the map). While the end follows the cursor, the unlocked values follow it.
    /// </summary>
    /// <returns><c>true</c> when anything changed.</returns>
    public bool Hover(GeoPosition? cursor)
    {
        _cursor = cursor;
        if (Phase != RangeBearingPhase.Following || cursor is not { } c)
            return false;
        if (!ApplyCursor(c))
            return false;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// A click at <paramref name="position"/>: places the origin, fixes the end,
    /// or starts again from a new origin.
    /// </summary>
    /// <returns><c>true</c> (a click always changes something).</returns>
    public bool Click(GeoPosition position)
    {
        _cursor = position;
        switch (Phase)
        {
            case RangeBearingPhase.Following:
                ApplyCursor(position);
                Phase = RangeBearingPhase.Fixed;
                break;
            default:
                Origin = position;
                ClearUnlocked();
                Phase = IsRangeLocked && IsBearingLocked ? RangeBearingPhase.Fixed : RangeBearingPhase.Following;
                break;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Enter: fixes the end where the cursor is.</summary>
    public bool Commit()
    {
        if (Phase != RangeBearingPhase.Following)
            return false;
        if (_cursor is { } cursor)
            ApplyCursor(cursor);
        Phase = RangeBearingPhase.Fixed;
        RaiseChanged();
        return true;
    }

    /// <summary>Backspace: back to placing the origin. Typed values stay locked.</summary>
    public bool Backstep()
    {
        if (Origin is null)
            return false;
        Origin = null;
        ClearUnlocked();
        Phase = RangeBearingPhase.PlacingOrigin;
        RaiseChanged();
        return true;
    }

    /// <summary>Delete: clears the origin and the locks.</summary>
    public bool Discard()
    {
        if (Origin is null && RangeMetres is null && BearingDegrees is null)
            return false;
        Reset();
        return true;
    }

    /// <summary>Clears everything but the geometry and the ring settings, as when the tool is turned off.</summary>
    public void Reset()
    {
        Origin = null;
        RangeMetres = null;
        BearingDegrees = null;
        IsRangeLocked = false;
        IsBearingLocked = false;
        Phase = RangeBearingPhase.PlacingOrigin;
        _cursor = null;
        RaiseChanged();
    }

    /// <summary>A typed origin. With both values locked the end is fixed; otherwise it follows the cursor.</summary>
    public void SetOrigin(GeoPosition origin)
    {
        Origin = origin;
        if (Phase == RangeBearingPhase.PlacingOrigin)
            Phase = RangeBearingPhase.Following;
        UpdateFixedForLocks();
        RaiseChanged();
    }

    /// <summary>A typed range in metres, which locks it; <c>null</c> unlocks it.</summary>
    public void SetRange(double? metres)
    {
        if (metres is { } m)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(m);
        RangeMetres = metres;
        IsRangeLocked = metres is not null;
        AfterLockChange();
    }

    /// <summary>A typed true bearing in degrees, which locks it; <c>null</c> unlocks it.</summary>
    public void SetBearing(double? degrees)
    {
        BearingDegrees = degrees is { } d ? Normalize(d) : null;
        IsBearingLocked = degrees is not null;
        AfterLockChange();
    }

    /// <summary>
    /// Unlocks the range, keeping its value until the cursor moves (the lock
    /// icon was clicked).
    /// </summary>
    public void UnlockRange()
    {
        if (!IsRangeLocked)
            return;
        IsRangeLocked = false;
        AfterLockChange();
    }

    /// <summary>Unlocks the bearing, keeping its value until the cursor moves.</summary>
    public void UnlockBearing()
    {
        if (!IsBearingLocked)
            return;
        IsBearingLocked = false;
        AfterLockChange();
    }

    /// <summary>Turns the rings on or off and sets their interval and count; <c>null</c> leaves a value as it is.</summary>
    public void SetRings(bool enabled, double? intervalMetres = null, int? count = null)
    {
        if (intervalMetres is { } interval)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval);
            RingIntervalMetres = interval;
        }

        if (count is { } n)
            RingCount = Math.Clamp(n, 1, MaxRingCount);
        RingsEnabled = enabled;
        RaiseChanged();
    }

    private void AfterLockChange()
    {
        // With a lock released while the end was fixed, the end follows the
        // cursor again, as an EBL or VRM does once it's unlocked.
        if (Origin is not null && Phase == RangeBearingPhase.Fixed && !(IsRangeLocked && IsBearingLocked))
            Phase = RangeBearingPhase.Following;
        UpdateFixedForLocks();
        RaiseChanged();
    }

    private void UpdateFixedForLocks()
    {
        if (Origin is not null && IsRangeLocked && IsBearingLocked)
            Phase = RangeBearingPhase.Fixed;
    }

    private void ClearUnlocked()
    {
        if (!IsRangeLocked)
            RangeMetres = null;
        if (!IsBearingLocked)
            BearingDegrees = null;
    }

    private bool ApplyCursor(GeoPosition cursor)
    {
        if (Origin is not { } origin || (IsRangeLocked && IsBearingLocked))
            return false;

        var changed = false;
        var distance = MarineGeodesy.DistanceMetres(Geometry, origin, cursor);
        if (distance <= 0)
            return false;
        if (!IsRangeLocked && RangeMetres != distance)
        {
            RangeMetres = distance;
            changed = true;
        }

        if (!IsBearingLocked)
        {
            var bearing = MarineGeodesy.InitialBearingDegrees(Geometry, origin, cursor);
            if (BearingDegrees != bearing)
            {
                BearingDegrees = bearing;
                changed = true;
            }
        }

        return changed;
    }

    // Leaves a bearing already in [0, 360) exactly as given, so results stay unrounded.
    private static double Normalize(double degrees) =>
        degrees is >= 0 and < 360 ? degrees : ((degrees % 360.0) + 360.0) % 360.0;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
