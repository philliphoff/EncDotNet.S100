using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EncDotNet.S100.Viewer.Tests.Headless;

/// <summary>
/// Hosts a real viewer view in a headless window and drives it the way a user
/// would: pointer clicks at a control's on-screen centre and keyboard input to
/// the focused control, with layout and dispatcher work flushed after each step.
/// Use from an <c>[AvaloniaFact]</c> test.
/// </summary>
/// <example>
/// <code>
/// using var host = ViewHost.Show(new DatasetsView { DataContext = datasets });
/// host.Click(host.Find&lt;TabItem&gt;("Datasets.Inspector.LayersTab"));
/// Assert.Equal(DatasetInspectorTab.Layers, datasets.InspectorTab);
/// </code>
/// </example>
internal sealed class ViewHost : IDisposable
{
    private ViewHost(Window window) => Window = window;

    /// <summary>The headless window that hosts the view.</summary>
    public Window Window { get; }

    /// <summary>
    /// Shows <paramref name="content"/> in a new headless window of the given
    /// size and lays it out. Styles and resources come from the test
    /// application (<see cref="HeadlessViewerApp"/>, the app's own XAML).
    /// </summary>
    public static ViewHost Show(Control content, double width = 1200, double height = 800)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };
        window.Show();
        var host = new ViewHost(window);
        host.Settle();
        return host;
    }

    /// <summary>
    /// Every control of type <typeparamref name="T"/> in the window's visual
    /// tree, in tree order, including those inside templates and popups' hosts.
    /// </summary>
    public IEnumerable<T> All<T>()
        where T : Visual
        => Window.GetVisualDescendants().OfType<T>();

    /// <summary>
    /// The single visible control of type <typeparamref name="T"/> matching
    /// <paramref name="predicate"/>. Fails the test when there are none or
    /// several, so a lookup never silently binds to the wrong control.
    /// </summary>
    public T Find<T>(Func<T, bool> predicate)
        where T : Control
        => Single(All<T>(), predicate);

    /// <summary>
    /// The single visible control whose <see cref="AutomationProperties.AutomationIdProperty"/>
    /// or, failing that, <see cref="StyledElement.Name"/> is <paramref name="id"/>.
    /// Unlike <c>FindControl</c> this crosses name scopes (nested user controls,
    /// templates), since a user sees one window, not scopes.
    /// </summary>
    public T Find<T>(string id)
        where T : Control
        => Find<T>(c => HasId(c, id));

    /// <summary>
    /// The single visible control with automation id <paramref name="id"/>
    /// inside <paramref name="within"/>. For ids that repeat once per item,
    /// such as a list row's buttons (<c>Datasets.Row.Remove</c>), scoped by
    /// that item's container.
    /// </summary>
    public T Find<T>(string id, Visual within)
        where T : Control
        => Single(within.GetVisualDescendants().OfType<T>(), c => HasId(c, id));

    private static bool HasId(Control control, string id)
        => AutomationProperties.GetAutomationId(control) == id || control.Name == id;

    private static T Single<T>(IEnumerable<T> candidates, Func<T, bool> predicate)
        where T : Control
    {
        var matches = candidates.Where(c => c.IsEffectivelyVisible && predicate(c)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No visible {typeof(T).Name} matched the predicate."),
            _ => throw new InvalidOperationException(
                $"{matches.Count} visible {typeof(T).Name} controls matched the predicate; narrow it."),
        };
    }

    /// <summary>
    /// Clicks the centre of <paramref name="control"/> with the left button,
    /// as a user would: the click lands on whatever is on top at that point.
    /// </summary>
    public void Click(Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = CenterOf(control);
        Window.MouseMove(point, modifiers);
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseUp(point, MouseButton.Left, modifiers);
        Settle();
    }

    /// <summary>Presses and releases <paramref name="key"/> on the focused control.</summary>
    public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPressQwerty(key, modifiers);
        Window.KeyReleaseQwerty(key, modifiers);
        Settle();
    }

    /// <summary>Types <paramref name="text"/> into the focused control.</summary>
    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Settle();
    }

    /// <summary>
    /// Runs queued dispatcher work, lays the window out and renders a frame, so
    /// the view reflects every binding update and input event raised so far.
    /// </summary>
    /// <remarks>
    /// The frame matters for input: pointer hit testing reads the compositor's
    /// scene, which only updates when a frame is rendered. The app's render
    /// timer ticks continuously; the headless one only ticks when forced, so
    /// without it a control that appeared since the last frame could not be
    /// clicked.
    /// </remarks>
    public void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose() => Window.Close();

    private Point CenterOf(Control control)
    {
        if (!control.IsEffectivelyVisible)
        {
            throw new InvalidOperationException(
                $"{control.GetType().Name} '{control.Name}' is not visible, so a user could not click it.");
        }

        var center = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        return control.TranslatePoint(center, Window)
            ?? throw new InvalidOperationException(
                $"{control.GetType().Name} '{control.Name}' is not in the host window.");
    }
}
