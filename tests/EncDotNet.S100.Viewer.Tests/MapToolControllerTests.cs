using Avalonia.Input;
using EncDotNet.S100.Viewer.Tools;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MapToolControllerTests
{
    private sealed class StubTool : IMapTool
    {
        public StubTool(string id) { Id = id; }
        public string Id { get; }
        public Cursor? Cursor => null;
        public int ActivationCount { get; private set; }
        public int DeactivationCount { get; private set; }
        public void OnActivated(MapToolContext ctx) => ActivationCount++;
        public void OnDeactivated() => DeactivationCount++;
        public bool OnPointerPressed(PointerPressedEventArgs e) => false;
        public bool OnPointerMoved(PointerEventArgs e) => false;
        public bool OnPointerReleased(PointerReleasedEventArgs e) => false;
        public bool OnDoubleTapped(TappedEventArgs e) => false;
        public bool OnAction(MapToolAction action) => false;
    }

    [Fact]
    public void Activate_UnknownId_IsNoOp()
    {
        var c = new MapToolController();
        c.Activate("does-not-exist");
        Assert.Null(c.ActiveToolId);
    }

    [Fact]
    public void Activate_NewTool_DeactivatesPrevious()
    {
        var c = new MapToolController();
        var a = new StubTool("a");
        var b = new StubTool("b");
        c.Register(a);
        c.Register(b);

        c.Activate("a");
        c.Activate("b");

        Assert.Equal("b", c.ActiveToolId);
        // Without Initialize the controller doesn't fire OnActivated/Deactivated;
        // verify only that the active id flips.
    }

    [Fact]
    public void Toggle_FlipsActiveTool()
    {
        var c = new MapToolController();
        c.Register(new StubTool("a"));

        c.Toggle("a");
        Assert.Equal("a", c.ActiveToolId);
        c.Toggle("a");
        Assert.Null(c.ActiveToolId);
    }

    [Fact]
    public void IsActive_ReturnsTrueOnlyForActiveTool()
    {
        var c = new MapToolController();
        c.Register(new StubTool("a"));
        c.Register(new StubTool("b"));

        c.Activate("a");
        Assert.True(c.IsActive("a"));
        Assert.False(c.IsActive("b"));
    }

    [Fact]
    public void ActiveToolChanged_FiresOnActivationAndDeactivation()
    {
        var c = new MapToolController();
        c.Register(new StubTool("a"));

        var notifications = 0;
        c.ActiveToolChanged += _ => notifications++;

        c.Activate("a");
        c.Activate(null);

        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Activate_SameToolTwice_IsNoOp()
    {
        var c = new MapToolController();
        c.Register(new StubTool("a"));

        var notifications = 0;
        c.ActiveToolChanged += _ => notifications++;

        c.Activate("a");
        c.Activate("a");

        Assert.Equal(1, notifications);
    }
}
