using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Time HUD (#712, handoff Part F): the Timeline's controls as a small bar
/// at the bottom centre of the map while the Timeline tab isn't shown. Its
/// slots have fixed widths, so the controls never move under the pointer
/// (design note time-overlays D4). Narrow maps drop the offset, then shorten
/// Go live to an icon (D10); clicking the time focuses the bar so the
/// Timeline's keys step it (F6).
/// </summary>
public partial class TimeHudView : UserControl
{
    /// <summary>Below this map width the offset is dropped.</summary>
    internal const double CompactWidth = 640;

    /// <summary>Below this map width Go live is shortened to an icon too.</summary>
    internal const double TinyWidth = 540;

    /// <summary>Opens the Timeline dock (the bar's panel button).</summary>
    public static readonly StyledProperty<ICommand?> DockCommandProperty =
        AvaloniaProperty.Register<TimeHudView, ICommand?>(nameof(DockCommand));

    /// <summary>
    /// True while the dock is open on another tab: the dock button then shows
    /// the Timeline tab rather than opening the dock (time-overlays D9).
    /// </summary>
    public static readonly StyledProperty<bool> IsDockOpenProperty =
        AvaloniaProperty.Register<TimeHudView, bool>(nameof(IsDockOpen));

    /// <summary>False where the dock can't show (a narrow window): the dock button is hidden (D9).</summary>
    public static readonly StyledProperty<bool> IsDockAvailableProperty =
        AvaloniaProperty.Register<TimeHudView, bool>(nameof(IsDockAvailable), true);

    /// <summary>The width of the map the HUD sits on.</summary>
    public static readonly StyledProperty<double> MapWidthProperty =
        AvaloniaProperty.Register<TimeHudView, double>(nameof(MapWidth), double.PositiveInfinity);

    public TimeHudView()
    {
        InitializeComponent();
        // The time is a button so the keyboard (Tab, then Enter or Space) and
        // automation clients (its invoke pattern) reach it as well as the pointer.
        if (this.FindControl<Button>("TimeText") is { } time)
            time.Click += (_, _) => Focus();
        if (this.FindControl<Button>("DockToggle") is { } dock)
            dock.Click += (_, _) => DockCommand?.Execute(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdateDockToggle()
    {
        if (this.FindControl<Button>("DockToggle") is not { } dock)
            return;
        var (name, tip) = IsDockOpen
            ? (Strings.Label_ShowTimeline, Strings.Tooltip_TimeHudShowTimeline)
            : (Strings.Label_OpenTimeline, Strings.Tooltip_TimeHudOpenTimeline);
        AutomationProperties.SetName(dock, name);
        AutomationProperties.SetHelpText(dock, tip);
        ToolTip.SetTip(dock, tip);
        dock.IsVisible = IsDockAvailable;
        if (this.FindControl<Border>("DockSeparator") is { } separator)
            separator.IsVisible = IsDockAvailable;
    }

    /// <inheritdoc cref="DockCommandProperty"/>
    public ICommand? DockCommand
    {
        get => GetValue(DockCommandProperty);
        set => SetValue(DockCommandProperty, value);
    }

    /// <inheritdoc cref="IsDockOpenProperty"/>
    public bool IsDockOpen
    {
        get => GetValue(IsDockOpenProperty);
        set => SetValue(IsDockOpenProperty, value);
    }

    /// <inheritdoc cref="IsDockAvailableProperty"/>
    public bool IsDockAvailable
    {
        get => GetValue(IsDockAvailableProperty);
        set => SetValue(IsDockAvailableProperty, value);
    }

    /// <inheritdoc cref="MapWidthProperty"/>
    public double MapWidth
    {
        get => GetValue(MapWidthProperty);
        set => SetValue(MapWidthProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsDockOpenProperty || change.Property == IsDockAvailableProperty)
            UpdateDockToggle();
        if (change.Property == MapWidthProperty)
        {
            Classes.Set("compact", MapWidth < CompactWidth);
            Classes.Set("tiny", MapWidth < TinyWidth);
        }
    }
}
