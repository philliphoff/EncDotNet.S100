namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// The undo/redo history of one <see cref="Route"/>. Every interactive and
/// agent edit runs through <see cref="Execute(string, Action)"/> (or a
/// <see cref="BeginGesture"/> scope for a drag), which records the route's
/// state before the edit as one undoable step.
/// </summary>
/// <remarks>
/// <para>
/// Steps are whole-route snapshots rather than inverse commands: a route is
/// small, and a snapshot covers every geometry and attribute edit (including
/// ones added later) without a per-edit inverse to keep in step with the model.
/// </para>
/// <para>
/// A step is recorded only when the route raised <see cref="Route.Changed"/>
/// during the edit, so a no-op edit leaves the history untouched. Edits nested
/// inside an open edit or gesture fold into the outer step. Each route owns its
/// own history, so switching the active route never mixes histories.
/// </para>
/// </remarks>
public sealed class RouteEditHistory
{
    /// <summary>The most steps kept on the undo stack; older steps are dropped.</summary>
    public const int MaxDepth = 200;

    private readonly Route _route;
    private readonly List<Step> _undo = new();
    private readonly List<Step> _redo = new();
    private int _depth;
    private bool _dirty;
    private string? _openLabel;
    private RouteSnapshot? _openSnapshot;

    internal RouteEditHistory(Route route)
    {
        _route = route;
        _route.Changed += (_, _) =>
        {
            if (_depth > 0)
                _dirty = true;
        };
    }

    /// <summary>True when there is a step to undo and no edit is open.</summary>
    public bool CanUndo => _depth == 0 && _undo.Count > 0;

    /// <summary>True when there is a step to redo and no edit is open.</summary>
    public bool CanRedo => _depth == 0 && _redo.Count > 0;

    /// <summary>Label of the step <see cref="Undo"/> would revert, or <c>null</c>.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    /// <summary>Label of the step <see cref="Redo"/> would replay, or <c>null</c>.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>Number of steps that can be undone.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Number of steps that can be redone.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>True while an edit or gesture is open.</summary>
    public bool IsEditOpen => _depth > 0;

    /// <summary>
    /// Raised when the undo or redo stack changes (a step was recorded, undone,
    /// redone or cleared). The route's own <see cref="Route.Changed"/> reports
    /// the model change; this reports the history change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Runs <paramref name="edit"/> against the route and records it as one
    /// undoable step labelled <paramref name="label"/> when it changed the route.
    /// </summary>
    /// <param name="label">A short description of the edit, e.g. <c>"move waypoint"</c>.</param>
    /// <param name="edit">The edit. It must change the route through the route's
    /// methods, or call <see cref="Route.NotifyChanged"/> after an in-place edit.</param>
    public void Execute(string label, Action edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Open(label);
        try
        {
            edit();
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Runs <paramref name="edit"/> and returns its result, recording it as one
    /// undoable step when it changed the route.
    /// </summary>
    /// <typeparam name="T">The edit's result type.</typeparam>
    /// <param name="label">A short description of the edit.</param>
    /// <param name="edit">The edit.</param>
    /// <returns>The value <paramref name="edit"/> returned.</returns>
    public T Execute<T>(string label, Func<T> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Open(label);
        try
        {
            return edit();
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Opens a gesture, such as a waypoint drag, whose many small edits make one
    /// undoable step. Dispose the returned scope to close the gesture.
    /// </summary>
    /// <param name="label">A short description of the gesture.</param>
    /// <returns>A scope that closes the gesture when disposed. Disposing it
    /// more than once has no further effect.</returns>
    public IDisposable BeginGesture(string label)
    {
        Open(label);
        return new GestureScope(this);
    }

    /// <summary>Reverts the most recent step.</summary>
    /// <returns><c>true</c> when a step was undone.</returns>
    public bool Undo() => Swap(_undo, _redo);

    /// <summary>Replays the most recently undone step.</summary>
    /// <returns><c>true</c> when a step was redone.</returns>
    public bool Redo() => Swap(_redo, _undo);

    /// <summary>Drops every undo and redo step.</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
            return;
        _undo.Clear();
        _redo.Clear();
        OnChanged();
    }

    private void Open(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (_depth++ > 0)
            return;
        _dirty = false;
        _openLabel = label;
        _openSnapshot = _route.CaptureSnapshot();
    }

    private void Close()
    {
        if (_depth == 0 || --_depth > 0)
            return;

        var snapshot = _openSnapshot!;
        var label = _openLabel!;
        _openSnapshot = null;
        _openLabel = null;
        if (!_dirty)
            return;

        _dirty = false;
        _undo.Add(new Step(label, snapshot));
        if (_undo.Count > MaxDepth)
            _undo.RemoveAt(0);
        _redo.Clear();
        OnChanged();
    }

    private bool Swap(List<Step> from, List<Step> to)
    {
        if (_depth > 0 || from.Count == 0)
            return false;

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(new Step(step.Label, _route.CaptureSnapshot()));
        _route.RestoreSnapshot(step.Snapshot);
        OnChanged();
        return true;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record Step(string Label, RouteSnapshot Snapshot);

    private sealed class GestureScope(RouteEditHistory history) : IDisposable
    {
        private RouteEditHistory? _history = history;

        public void Dispose()
        {
            _history?.Close();
            _history = null;
        }
    }
}

/// <summary>
/// A deep copy of a route's waypoints, legs, route-level info and passage, captured
/// before an edit so <see cref="RouteEditHistory"/> can restore it.
/// </summary>
internal sealed record RouteSnapshot(
    IReadOnlyList<RouteWaypoint> Waypoints,
    IReadOnlyList<RouteLeg> Legs,
    RouteInfo Info,
    RoutePassage Passage);
