using Avalonia.Input;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// "Cursor Pick" tool. Declarative wrapper around the pre-existing
/// pick-mode flag; the actual pick gesture handling continues to live in
/// <c>MapInteractionController</c> (long-press, modifier-click, single-tap
/// in pick mode), which checks
/// <see cref="ViewModels.MainViewModel.IsPickModeActive"/> — that flag is
/// derived from <see cref="MapToolController.ActiveToolId"/> being
/// <c>"pick"</c>.
/// </summary>
/// <remarks>
/// All <see cref="IMapTool"/> event handlers return <c>false</c> so that
/// the existing pick gesture pipeline in <c>MapInteractionController</c>
/// continues to fire while this tool is active. The tool exists so that
/// Pick Mode and Measure Mode share the same activation/deactivation
/// machinery.
/// </remarks>
internal sealed class PickTool : IMapTool
{
    public const string ToolId = "pick";

    public string Id => ToolId;

    private Cursor? _cursor;
    /// <summary>
    /// Cross-hair cursor; lazily created so the type can be instantiated
    /// before the Avalonia platform is up (e.g. in unit tests).
    /// </summary>
    public Cursor? Cursor => _cursor ??= new Cursor(StandardCursorType.Cross);

    public void OnActivated(MapToolContext context) { }

    public void OnDeactivated() { }

    public bool OnPointerPressed(PointerPressedEventArgs e) => false;
    public bool OnPointerMoved(PointerEventArgs e) => false;
    public bool OnPointerReleased(PointerReleasedEventArgs e) => false;
    public bool OnDoubleTapped(TappedEventArgs e) => false;
    public bool OnAction(MapToolAction action) => false;
}
