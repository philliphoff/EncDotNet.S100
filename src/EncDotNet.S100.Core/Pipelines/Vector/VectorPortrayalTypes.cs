using System.Collections.ObjectModel;
using EncDotNet.S100.Collections;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// A named portrayal script (source text) from a vector portrayal catalogue.
/// </summary>
public sealed class Script
{
    /// <summary>The script's name as referenced by the catalogue.</summary>
    public required string Name { get; init; }

    /// <summary>The script source text.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// An S-100 Part 9 point symbol, as loaded from the portrayal catalogue's
/// <c>Symbols</c> folder.
/// </summary>
public sealed class SvgSymbol
{
    /// <summary>The symbol identifier as referenced by drawing instructions (e.g. <c>"BOYLAT13"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The raw SVG document text. Colour classes in it are resolved against
    /// the active palette at render time.
    /// </summary>
    public required string SvgContent { get; init; }

    /// <summary>
    /// X coordinate of the symbol's pivot (anchor) point, in the SVG's user
    /// units. Defaults to <c>0</c>; the bundled catalogues do not set it.
    /// </summary>
    public double PivotX { get; init; }

    /// <summary>
    /// Y coordinate of the symbol's pivot (anchor) point, in the SVG's user
    /// units. Defaults to <c>0</c>; the bundled catalogues do not set it.
    /// </summary>
    public double PivotY { get; init; }
}

/// <summary>
/// An S-100 Part 9 line style (pen and optional dash pattern), as loaded from
/// the portrayal catalogue's line-style files and referenced by name from
/// line drawing instructions.
/// </summary>
public sealed class LineStyle
{
    /// <summary>The line style identifier as referenced by drawing instructions.</summary>
    public required string Name { get; init; }

    /// <summary>Pen width, in millimetres.</summary>
    public required float Width { get; init; }

    /// <summary>
    /// Pen colour: normally an S-100 colour token resolved against the
    /// active palette at render time; <c>"#000000"</c> when the file
    /// declares none.
    /// </summary>
    public required string Color { get; init; }

    /// <summary>
    /// Dash definition as a flat list of (start offset, length) pairs, in
    /// millimetres, or <see langword="null"/> for a solid line.
    /// </summary>
    public float[]? DashPattern { get; init; }
}

/// <summary>
/// An S-100 Part 9 area fill: a solid colour and/or a symbol pattern tiled
/// along two lattice vectors. Referenced by name from area drawing
/// instructions.
/// </summary>
public sealed class AreaFill
{
    /// <summary>The area fill identifier as referenced by drawing instructions.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Solid fill colour declared by the file, or <see langword="null"/> when
    /// the fill is pattern-only.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Name of the <see cref="SvgSymbol"/> tiled across the area, or
    /// <see langword="null"/> when the fill has no pattern.
    /// </summary>
    public string? PatternSymbol { get; init; }
    /// <summary>Tiling vector 1 (x component, in mm).</summary>
    public double V1X { get; init; }
    /// <summary>Tiling vector 1 (y component, in mm).</summary>
    public double V1Y { get; init; }
    /// <summary>Tiling vector 2 (x component, in mm).</summary>
    public double V2X { get; init; }
    /// <summary>Tiling vector 2 (y component, in mm).</summary>
    public double V2Y { get; init; }
}

/// <summary>
/// Controls visibility of features by viewing group assignment.
/// </summary>
/// <remarks>
/// <para>
/// Effective visibility is the layered combination of two inputs
/// (S-100 Part 9 §11.7, "Display modes and viewing groups"):
/// </para>
/// <list type="bullet">
///   <item>
///     A <em>base mode membership</em> set established by the active
///     <see cref="DisplayModeController"/>: when a non-null set is
///     active, only viewing groups in the set are considered base-on
///     and every other viewing group is base-off. When the membership
///     is <c>null</c> (the default), every viewing group is base-on.
///   </item>
///   <item>
///     <em>User overrides</em> applied on top of the base via
///     <see cref="SetUserOverride"/>. An explicit override always wins
///     over the base membership, allowing the user to toggle individual
///     groups regardless of the active mode.
///   </item>
/// </list>
/// </remarks>
public sealed class ViewingGroupController
{
    private readonly Dictionary<int, bool> _userOverrides = new();
    private IReadOnlySet<int>? _modeMembership;

    /// <summary>
    /// Raised whenever effective visibility changes — either because
    /// the active mode membership was updated, or because a user
    /// override was set or cleared.
    /// </summary>
    public event Action? Changed;

    /// <summary>Read-only view of the current per-group user overrides.</summary>
    public IReadOnlyDictionary<int, bool> UserOverrides => new ReadOnlyDictionary<int, bool>(_userOverrides);

    /// <summary>
    /// Backwards-compatible view: the effective visibility for every
    /// viewing group that has either an active-mode membership entry
    /// or an explicit user override. Groups that are visible solely by
    /// the "no mode active → everything visible" default are not
    /// enumerated.
    /// </summary>
    public IReadOnlyDictionary<int, bool> GroupVisibility
    {
        get
        {
            var view = new Dictionary<int, bool>();
            if (_modeMembership is not null)
            {
                foreach (var id in _modeMembership)
                {
                    view[id] = true;
                }
            }
            foreach (var (id, visible) in _userOverrides)
            {
                view[id] = visible;
            }
            return view;
        }
    }

    /// <summary>
    /// Sets the user's explicit visibility override for
    /// <paramref name="viewingGroup"/>. Equivalent to calling
    /// <see cref="SetUserOverride(int, bool?)"/> with a non-null value.
    /// </summary>
    public void SetVisible(int viewingGroup, bool visible) =>
        SetUserOverride(viewingGroup, visible);

    /// <summary>
    /// Sets, replaces, or clears the user override for
    /// <paramref name="viewingGroup"/>. Pass <c>null</c> to clear so
    /// that the base mode membership wins again.
    /// </summary>
    public void SetUserOverride(int viewingGroup, bool? visible)
    {
        bool changed;
        if (visible is null)
        {
            changed = _userOverrides.Remove(viewingGroup);
        }
        else
        {
            changed = !_userOverrides.TryGetValue(viewingGroup, out var existing)
                || existing != visible.Value;
            _userOverrides[viewingGroup] = visible.Value;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Clears every user override.</summary>
    public void ClearUserOverrides()
    {
        if (_userOverrides.Count == 0) return;
        _userOverrides.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the active-mode viewing-group membership. Pass
    /// <c>null</c> to clear so that every viewing group is visible by
    /// default again. User overrides are preserved.
    /// </summary>
    public void SetActiveModeMembership(IReadOnlySet<int>? membership)
    {
        // Treat reference equality as a no-op fast path; otherwise the
        // event always fires so listeners can re-render. Cheap content
        // equality isn't worth it here — membership sets are typically
        // immutable post-construction.
        if (ReferenceEquals(_modeMembership, membership)) return;
        _modeMembership = membership;
        Changed?.Invoke();
    }

    /// <summary>
    /// The active mode membership, or <c>null</c> when no mode is
    /// active (in which case every viewing group is visible by
    /// default). The returned set is a non-downcastable read-only view.
    /// </summary>
    public IReadOnlySet<int>? ActiveModeMembership =>
        _modeMembership is null ? null : new ReadOnlySetView<int>(_modeMembership);

    /// <summary>
    /// Returns the effective visibility for <paramref name="viewingGroup"/>.
    /// User overrides win over base mode membership; with no override and
    /// no active mode, the group is visible.
    /// </summary>
    public bool IsVisible(int viewingGroup)
    {
        if (_userOverrides.TryGetValue(viewingGroup, out var overrideValue))
        {
            return overrideValue;
        }

        return _modeMembership is null || _modeMembership.Contains(viewingGroup);
    }
}

/// <summary>
/// Controls visibility of drawing instructions by display plane
/// (S-100 Part 9 §11.6). The three canonical planes are
/// <see cref="DisplayPlane.UnderRadar"/> and
/// <see cref="DisplayPlane.OverRadar"/>; all planes are visible by
/// default.
/// </summary>
/// <remarks>
/// Unlike <see cref="ViewingGroupController"/> this controller has no
/// "mode membership" layer — planes are simply on or off. The pipeline
/// evaluates the controller after viewing-group filtering so the
/// (typically smaller) VG-filtered list is the input.
/// </remarks>
public sealed class DisplayPlaneController
{
    private readonly HashSet<DisplayPlane> _hidden = new();
    private readonly ReadOnlySetView<DisplayPlane> _hiddenView;

    /// <summary>Creates a new <see cref="DisplayPlaneController"/>.</summary>
    public DisplayPlaneController()
    {
        _hiddenView = new ReadOnlySetView<DisplayPlane>(_hidden);
    }

    /// <summary>
    /// Raised whenever the hidden set changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// The set of planes that are currently hidden. The returned set is a
    /// non-downcastable read-only view over the live hidden set.
    /// </summary>
    public IReadOnlySet<DisplayPlane> HiddenPlanes => _hiddenView;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="plane"/> is visible
    /// (i.e. not in the hidden set).
    /// </summary>
    public bool IsVisible(DisplayPlane plane) => !_hidden.Contains(plane);

    /// <summary>
    /// Shows or hides <paramref name="plane"/>. Raises
    /// <see cref="Changed"/> when the effective state changes.
    /// </summary>
    public void SetVisible(DisplayPlane plane, bool visible)
    {
        bool changed = visible ? _hidden.Remove(plane) : _hidden.Add(plane);
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Resets all planes to visible.
    /// </summary>
    public void ShowAll()
    {
        if (_hidden.Count == 0) return;
        _hidden.Clear();
        Changed?.Invoke();
    }
}

/// <summary>
/// Tracks the active S-100 Part 9 display-mode id for a vector
/// portrayal catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Per S-100 Part 9 §11.7, a portrayal catalogue may declare one or
/// more <c>&lt;displayMode&gt;</c> elements (e.g. <c>DisplayBase</c>,
/// <c>StandardDisplay</c>, <c>OtherInformation</c> for S-101). Each
/// mode references a set of <c>&lt;viewingGroupLayer&gt;</c> ids,
/// and each layer references a set of integer viewing groups. The
/// active mode determines the base on/off state of every viewing
/// group; per-group user overrides (handled by
/// <see cref="ViewingGroupController"/>) layer on top.
/// </para>
/// <para>
/// This controller carries only the chosen mode id. The per-spec
/// catalogue subscribes to <see cref="Changed"/> and translates the
/// id into a concrete viewing-group set, which it pushes into its
/// paired <see cref="ViewingGroupController"/>.
/// </para>
/// </remarks>
public sealed class DisplayModeController
{
    /// <summary>
    /// The id of the active display mode, or <c>null</c> when no mode
    /// is active (interpreted as "all viewing groups visible").
    /// </summary>
    public string? ActiveDisplayModeId { get; private set; }

    private IReadOnlySet<string> _declaredModeIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The set of display-mode ids declared by the per-spec portrayal
    /// catalogue this controller is bound to. Empty until
    /// <see cref="SetDeclaredModeIds"/> is called (typically by
    /// <c>DisplayModeMembership.Bind</c>). The viewer uses this to
    /// decide which ECDIS categories make sense for a given spec. The
    /// returned set is a non-downcastable read-only view.
    /// </summary>
    public IReadOnlySet<string> DeclaredModeIds => new ReadOnlySetView<string>(_declaredModeIds);

    /// <summary>
    /// Raised whenever <see cref="ActiveDisplayModeId"/> changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Sets <see cref="ActiveDisplayModeId"/>. Pass <c>null</c> to
    /// clear (every viewing group visible by default).
    /// </summary>
    public void SetActive(string? displayModeId)
    {
        if (string.Equals(ActiveDisplayModeId, displayModeId, StringComparison.Ordinal))
            return;

        ActiveDisplayModeId = displayModeId;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records the catalogue-declared mode ids. Does not raise
    /// <see cref="Changed"/>; this is metadata only.
    /// </summary>
    public void SetDeclaredModeIds(IReadOnlySet<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _declaredModeIds = ids;
    }
}
