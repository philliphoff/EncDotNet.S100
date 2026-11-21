using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A map overlay button with a pressed state: the <c>:checked</c> pseudo-class
/// while <see cref="IsChecked"/> is true (styled with the accent fill), and the
/// toggle pattern for automation, so screen readers and UI automation read it as
/// a pressed button (<c>aria-pressed</c>). Clicking runs <see cref="Button.Command"/>;
/// the state comes only from the binding, never from the click itself.
/// </summary>
/// <remarks>
/// A <see cref="Button"/> rather than a <c>ToggleButton</c> so ShadUI's
/// <c>Classes="Icon"</c> template applies and the button looks like its
/// neighbours in the overlay column at rest.
/// </remarks>
public class MapToggleButton : Button
{
    /// <summary>Defines the <see cref="IsChecked"/> property.</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<MapToggleButton, bool>(nameof(IsChecked));

    static MapToggleButton()
    {
        IsCheckedProperty.Changed.AddClassHandler<MapToggleButton>((button, e) => button.OnIsCheckedChanged(e));
    }

    /// <summary>Whether the button shows as pressed.</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Button);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new MapToggleButtonAutomationPeer(this);

    private void OnIsCheckedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var isChecked = e.GetNewValue<bool>();
        PseudoClasses.Set(":checked", isChecked);
        (ControlAutomationPeer.FromElement(this) as MapToggleButtonAutomationPeer)?.RaiseToggleStateChanged(
            e.GetOldValue<bool>(), isChecked);
    }

    private void ClickFromAutomation() => OnClick();

    private sealed class MapToggleButtonAutomationPeer(MapToggleButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsChecked ? ToggleState.On : ToggleState.Off;

        public void Toggle()
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();
            owner.ClickFromAutomation();
        }

        public void RaiseToggleStateChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(
                TogglePatternIdentifiers.ToggleStateProperty,
                oldValue ? ToggleState.On : ToggleState.Off,
                newValue ? ToggleState.On : ToggleState.Off);
    }
}
