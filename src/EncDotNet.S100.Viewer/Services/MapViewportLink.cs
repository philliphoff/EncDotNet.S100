using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What the viewport link needs of a map (split map, #1734): where it looks,
/// in Web Mercator, when that changes, a way to move it at once, and its
/// demand planner.
/// </summary>
internal interface ILinkableMapView
{
    /// <summary>The map's id.</summary>
    MapViewId Id { get; }

    /// <summary>The map's demand planner.</summary>
    DemandPlanner Planner { get; }

    /// <summary>Where the map looks, or <see langword="null"/> before it's laid out.</summary>
    ViewState? CurrentView { get; }

    /// <summary>
    /// Raised, on the UI thread, every time the map's view changes: each frame
    /// of a drag, a fling or an animation, and each change the link makes.
    /// </summary>
    event EventHandler? ViewChanged;

    /// <summary>Moves the map at once, through its zoom and pan limits.</summary>
    /// <param name="centerX">The centre's x, EPSG:3857 metres.</param>
    /// <param name="centerY">The centre's y, EPSG:3857 metres.</param>
    /// <param name="resolution">Metres per pixel.</param>
    void ShowView(double centerX, double centerY, double resolution);
}

/// <summary>
/// The viewport link (split map, #1734; design note D13, D14): while it's on,
/// the maps share their centre and keep the ratio of their scales from the
/// moment of linking.
/// </summary>
/// <remarks>
/// <para>
/// <b>How a pane follows (architecture §5 question 1).</b> Whichever map
/// moves on its own (the one being dragged, scrolled, flung or animated) is
/// the driver. Every frame its view changes, the others are set at once to
/// the driver's centre and to the driver's resolution times their ratio,
/// through <see cref="ILinkableMapView.ShowView"/> (Mapsui's
/// <c>SetViewportToCenterAndResolution</c>). Only the driver runs Mapsui's
/// gesture, fling or animation; the followers never animate themselves, so
/// two animations can't fight.
/// </para>
/// <para>
/// <b>What the followers' planners see.</b> Copied motion, not a jump: a
/// follower's navigator reports every frame it's set to its own planner, so
/// its view history has the same velocity and zoom rate as the driver's, at
/// its own scale. And when the driver's planner learns a destination (the
/// predicted end of a fling, or an animation's target), the link hands each
/// follower's planner the same destination, mapped into that follower, as an
/// animation that ends when the driver's does. The follower then pre-fetches
/// its tiles and cells for where it will come to rest, as the driver does.
/// </para>
/// <para>
/// The link is the driver's view mapped through the ratios, not a sum of
/// steps: a follower held back by its zoom limits rejoins its ratio when the
/// driver comes back. Unlinking moves nothing. A map that joins while linked
/// (a new pane, which opens at the active pane's view) joins at the active
/// map's ratio and is moved to the link once it's laid out.
/// </para>
/// </remarks>
internal sealed class MapViewportLink
{
    private readonly MapViews _views;
    private readonly Dictionary<MapViewId, Member> _members = [];
    private bool _isLinked;
    private double _centerX;
    private double _centerY;
    private double _resolution;
    private bool _applying;

    /// <summary>Creates the link over <paramref name="views"/>; it starts off (D13).</summary>
    /// <param name="views">The maps in the window.</param>
    public MapViewportLink(MapViews views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
        _views.ViewAdded += (_, view) => OnViewAdded(view);
        _views.ViewRemoved += (_, view) => OnViewRemoved(view);
    }

    /// <summary>Whether the maps are linked.</summary>
    public bool IsLinked => _isLinked;

    /// <summary>Raised after <see cref="IsLinked"/> changes.</summary>
    public event EventHandler? IsLinkedChanged;

    /// <summary>
    /// The resolution ratio a map keeps to the link while linked (its
    /// resolution over the link's), or <see langword="null"/> when unlinked or
    /// the map isn't in the link.
    /// </summary>
    /// <param name="id">The map's id.</param>
    /// <returns>The ratio.</returns>
    public double? RatioOf(MapViewId id) =>
        _isLinked && _members.TryGetValue(id, out var member) && member.Ratio is { } ratio ? ratio : null;

    /// <summary>
    /// Turns the link on or off. On: the other maps centre on the active
    /// map's centre, each keeping its own scale, and the ratios of the scales
    /// are kept from now on (D14). Off: nothing moves.
    /// </summary>
    /// <param name="linked">Whether to link.</param>
    public void SetLinked(bool linked)
    {
        if (linked == _isLinked)
            return;

        if (linked)
            Link();
        else
            Unlink();
        IsLinkedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Link()
    {
        var maps = _views.All.OfType<ILinkableMapView>().ToList();
        var anchor = _views.Active is ILinkableMapView { CurrentView: not null } active
            ? active
            : maps.FirstOrDefault(m => m.CurrentView is not null);

        _isLinked = true;
        if (anchor?.CurrentView is not { } anchorView)
        {
            // Nothing is laid out yet: each map joins as it's laid out.
            foreach (var map in maps)
                Join(map, ratio: null);
            return;
        }

        _centerX = anchorView.CenterX;
        _centerY = anchorView.CenterY;
        _resolution = anchorView.Resolution;
        foreach (var map in maps)
        {
            double? ratio = map.CurrentView is { Resolution: > 0 } view ? view.Resolution / _resolution : null;
            Join(map, ratio);
        }

        ApplyAll(driver: anchor);
    }

    private void Unlink()
    {
        _isLinked = false;
        foreach (var member in _members.Values)
            member.Leave();
        _members.Clear();
    }

    private void OnViewAdded(IMapViewCapabilities view)
    {
        if (!_isLinked || view is not ILinkableMapView map || _members.ContainsKey(map.Id))
            return;

        // A new pane opens at the active pane's view (D2), so it joins at the
        // active map's ratio; it's moved to the link once it's laid out.
        double? ratio = _views.Active is ILinkableMapView active && _members.TryGetValue(active.Id, out var activeMember)
            ? activeMember.Ratio
            : null;
        var member = Join(map, ratio ?? 1);
        if (map.CurrentView is null)
            member.AwaitingLayout = true;
        else
            Apply(member);
    }

    private void OnViewRemoved(IMapViewCapabilities view)
    {
        if (_members.Remove(view.Id, out var member))
            member.Leave();
    }

    private Member Join(ILinkableMapView map, double? ratio)
    {
        var member = new Member(this, map) { Ratio = ratio };
        _members[map.Id] = member;
        member.Enter();
        return member;
    }

    private void OnViewChanged(Member member)
    {
        if (_applying || !_isLinked || !_members.ContainsKey(member.Map.Id)
            || member.Map.CurrentView is not { Resolution: > 0 } view)
            return;

        if (member.Ratio is not { } ratio)
        {
            // The first view of a map that joined before anything was laid out.
            if (_resolution <= 0)
            {
                _centerX = view.CenterX;
                _centerY = view.CenterY;
                _resolution = view.Resolution;
                member.Ratio = 1;
                return;
            }

            member.Ratio = view.Resolution / _resolution;
            member.Applied = view;
            return;
        }

        if (member.AwaitingLayout)
        {
            // A map that joined while linked: the link moves it, not the other way.
            member.AwaitingLayout = false;
            Apply(member);
            return;
        }

        // A view the link set, reported again (a resize, a posted re-apply):
        // not a move of its own.
        if (member.Applied is { } applied && SameView(applied, view))
            return;

        _centerX = view.CenterX;
        _centerY = view.CenterY;
        _resolution = view.Resolution / ratio;
        member.Applied = view;
        ApplyAll(driver: member.Map);
    }

    private void OnDestinationChanged(Member driver)
    {
        if (_applying || !_isLinked || driver.Ratio is not { } driverRatio)
            return;

        var motion = driver.Map.Planner.Motion;
        if (!motion.HasTarget || motion.Target is not { Resolution: > 0 } target)
            return;

        var now = Stopwatch.GetTimestamp();
        var remaining = Stopwatch.GetElapsedTime(now, motion.ArrivalTimestamp);
        if (remaining <= TimeSpan.Zero)
            return;

        var linkResolution = target.Resolution / driverRatio;
        _applying = true;
        try
        {
            foreach (var member in _members.Values)
            {
                if (ReferenceEquals(member, driver) || member.Ratio is not { } ratio
                    || member.Map.CurrentView is not { } current)
                    continue;
                member.Map.Planner.BeginAnimation(
                    current with
                    {
                        CenterX = target.CenterX,
                        CenterY = target.CenterY,
                        Resolution = linkResolution * ratio,
                    },
                    remaining,
                    now);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyAll(ILinkableMapView driver)
    {
        foreach (var member in _members.Values)
        {
            if (!ReferenceEquals(member.Map, driver))
                Apply(member);
        }
    }

    private void Apply(Member member)
    {
        if (member.Ratio is not { } ratio || member.Map.CurrentView is null || _resolution <= 0)
            return;

        _applying = true;
        try
        {
            member.Map.ShowView(_centerX, _centerY, _resolution * ratio);
        }
        finally
        {
            _applying = false;
        }

        member.Applied = member.Map.CurrentView;
    }

    // Centre within a pixel and the same resolution: what the link set.
    private static bool SameView(ViewState a, ViewState b)
    {
        var tolerance = Math.Max(a.Resolution, b.Resolution) * 0.5;
        return Math.Abs(a.CenterX - b.CenterX) <= tolerance
            && Math.Abs(a.CenterY - b.CenterY) <= tolerance
            && Math.Abs(a.Resolution - b.Resolution) <= 1e-9 * Math.Max(a.Resolution, b.Resolution);
    }

    private sealed class Member(MapViewportLink link, ILinkableMapView map)
    {
        private EventHandler? _viewChanged;
        private EventHandler? _destinationChanged;

        public ILinkableMapView Map { get; } = map;

        /// <summary>The map's resolution over the link's, once known.</summary>
        public double? Ratio { get; set; }

        /// <summary>The view the link last set or took from this map.</summary>
        public ViewState? Applied { get; set; }

        /// <summary>Whether the map joined while linked and hasn't been moved to the link yet.</summary>
        public bool AwaitingLayout { get; set; }

        public void Enter()
        {
            _viewChanged = (_, _) => link.OnViewChanged(this);
            _destinationChanged = (_, _) => link.OnDestinationChanged(this);
            Map.ViewChanged += _viewChanged;
            Map.Planner.DestinationChanged += _destinationChanged;
        }

        public void Leave()
        {
            Map.ViewChanged -= _viewChanged;
            Map.Planner.DestinationChanged -= _destinationChanged;
        }
    }
}
