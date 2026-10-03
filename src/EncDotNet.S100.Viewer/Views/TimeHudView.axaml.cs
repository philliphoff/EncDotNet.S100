using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Time HUD (#712, handoff Part F): the Timeline's controls as a small bar
/// at the bottom centre of the map while the dock is closed. Narrow maps drop
/// the offset, then the PINNED label (F5); clicking the time focuses the bar
/// so the Timeline's keys step it (F6).
/// </summary>
public partial class TimeHudView : UserControl
{
    /// <summary>Below this map width the offset is dropped.</summary>
    internal const double CompactWidth = 640;

    /// <summary>Below this map width the PINNED label is dropped too.</summary>
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
        if (this.FindControl<TextBlock>("TimeText") is { } time)
            time.PointerPressed += (_, e) =>
            {
                Focus(NavigationMethod.Pointer);
                e.Handled = true;
            };
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
