using System.ComponentModel;
using Avalonia.Input;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Owns the registry of available <see cref="IMapTool"/>s and the
/// currently-active tool. Routes pointer / key events from the host to
/// the active tool and notifies listeners (typically the view-model) when
/// the active tool changes.
/// </summary>
internal sealed class MapToolController : INotifyPropertyChanged
{
    private readonly Dictionary<string, IMapTool> _tools = new(StringComparer.Ordinal);
    private MapToolContext? _context;
    private IMapTool? _activeTool;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the active tool changes. The argument is the new
    /// active tool, or <c>null</c> when no tool is active. Subscribers
    /// typically use this to update the map cursor.
    /// </summary>
    public event Action<IMapTool?>? ActiveToolChanged;

    /// <summary>Currently active tool, or <c>null</c> when none.</summary>
    public IMapTool? ActiveTool => _activeTool;

    /// <summary>
    /// Convenience for view-model bindings: identifier of the active tool
    /// or <c>null</c>.
    /// </summary>
    public string? ActiveToolId => _activeTool?.Id;

    /// <summary>The map the tools take their input from, or <see langword="null"/> before <see cref="Initialize"/>.</summary>
    public MapToolContext? Context => _context;

    /// <summary>
    /// Provides the controller with the live <see cref="MapToolContext"/>
    /// once the visual tree is built. Until this is called, tool
    /// activation is recorded but tools are not actually invoked. After
    /// this call, any pre-recorded active tool is fully activated. Called
    /// again with another map's context (the active map of a split, or the
    /// one an event comes from), the active tool keeps its state and takes
    /// its input from that map (split map design note D12).
    /// </summary>
    public void Initialize(MapToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ReferenceEquals(_context, context))
            return;
        var first = _context is null;
        _context = context;

        if (_activeTool is null)
            return;
        if (first)
            _activeTool.OnActivated(context);
        else
            _activeTool.OnContextChanged(context);
    }

    /// <summary>Registers a tool. Tools are typically registered once at startup.</summary>
    public void Register(IMapTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Id] = tool;
    }

    /// <summary>
    /// Activates the tool with the given id, or deactivates the current
    /// tool when <paramref name="id"/> is <c>null</c>. Activating the
    /// already-active tool is a no-op; activating an unknown id is a
    /// no-op.
    /// </summary>
    public void Activate(string? id)
    {
        IMapTool? next = null;
        if (id is not null && !_tools.TryGetValue(id, out next))
            return;

        if (ReferenceEquals(next, _activeTool))
            return;

        if (_activeTool is not null && _context is not null)
            _activeTool.OnDeactivated();

        _activeTool = next;

        if (_activeTool is not null && _context is not null)
            _activeTool.OnActivated(_context);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveToolId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveTool)));
        ActiveToolChanged?.Invoke(_activeTool);
    }

    /// <summary>Convenience: activate / deactivate a tool by id.</summary>
    public void Toggle(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Activate(string.Equals(_activeTool?.Id, id, StringComparison.Ordinal) ? null : id);
    }

    /// <summary>Returns true when the tool with the given id is currently active.</summary>
    public bool IsActive(string id) => string.Equals(_activeTool?.Id, id, StringComparison.Ordinal);

    // --- Pointer/key forwarding ---------------------------------------
    // Pointer events carry the map they come from: in a split the tools take
    // input from any map, so a measurement can start in one and end in
    // another (D12, O2).

    public bool OnPointerPressed(MapToolContext source, PointerPressedEventArgs e) =>
        From(source) is { } t && t.OnPointerPressed(e);

    public bool OnPointerMoved(MapToolContext source, PointerEventArgs e) =>
        From(source) is { } t && t.OnPointerMoved(e);

    public bool OnPointerReleased(MapToolContext source, PointerReleasedEventArgs e) =>
        From(source) is { } t && t.OnPointerReleased(e);

    public bool OnDoubleTapped(MapToolContext source, TappedEventArgs e) =>
        From(source) is { } t && t.OnDoubleTapped(e);

    public bool OnAction(MapToolAction action) =>
        _activeTool is { } t && t.OnAction(action);

    private IMapTool? From(MapToolContext source)
    {
        if (_activeTool is null)
            return null;
        Initialize(source);
        return _activeTool;
    }
}
