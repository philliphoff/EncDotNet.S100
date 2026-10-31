using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.ViewModels;

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
        // D5: Live is always there; clicking it while live does nothing, rather
        // than greying it out as a disabled command would.
        if (this.FindControl<Button>("LiveToggle") is { } live)
        {
            live.Click += (_, _) =>
            {
                if (DataContext is TimelineViewModel timeline && timeline.NowCommand.CanExecute(null))
                    timeline.NowCommand.Execute(null);
            };
        }
        if (this.FindControl<Button>("DockToggle") is { } dock)
            dock.Click += (_, _) => DockCommand?.Execute(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc cref="DockCommandProperty"/>
    public ICommand? DockCommand
    {
        get => GetValue(DockCommandProperty);
        set => SetValue(DockCommandProperty, value);
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
        if (change.Property == MapWidthProperty)
        {
            Classes.Set("compact", MapWidth < CompactWidth);
            Classes.Set("tiny", MapWidth < TinyWidth);
        }
    }
}
