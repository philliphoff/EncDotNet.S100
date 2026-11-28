using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A map pane's number chip (split map step 2 §2.6, D18): filled while
/// <see cref="IsChecked"/> is true, half-filled while it is null, outlined
/// while false. Clicking runs <see cref="Button.Command"/>; Alt-click
/// (Option-click on macOS) runs <see cref="AltCommand"/>. The state comes only
/// from the binding, never from the click itself.
/// </summary>
/// <remarks>
/// It reads as a toggle button to screen readers and the <c>ui_*</c> tools
/// (the toggle pattern, half-filled being indeterminate), so a toggle through
/// automation is a plain click. Alt-click is a pointer shortcut; its keyboard
/// and automation path is the row's <b>Show in</b> menu.
/// </remarks>
public class PaneChip : Button
{
    /// <summary>Defines the <see cref="IsChecked"/> property.</summary>
    public static readonly StyledProperty<bool?> IsCheckedProperty =
        AvaloniaProperty.Register<PaneChip, bool?>(nameof(IsChecked), defaultValue: false);

    /// <summary>Defines the <see cref="AltCommand"/> property.</summary>
    public static readonly StyledProperty<ICommand?> AltCommandProperty =
        AvaloniaProperty.Register<PaneChip, ICommand?>(nameof(AltCommand));

    private bool _altPressed;

    static PaneChip()
    {
        IsCheckedProperty.Changed.AddClassHandler<PaneChip>((chip, e) => chip.OnIsCheckedChanged(e));
    }

    /// <summary>Creates a chip, outlined.</summary>
    public PaneChip() => UpdatePseudoClasses(false);

    /// <summary>Whether the pane shows the row: true, false, or null for some of it.</summary>
    public bool? IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>The command an Alt-click (Option-click on macOS) runs, with <see cref="Button.CommandParameter"/>.</summary>
    public ICommand? AltCommand
    {
        get => GetValue(AltCommandProperty);
        set => SetValue(AltCommandProperty, value);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new PaneChipAutomationPeer(this);

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _altPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        base.OnPointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _altPressed |= e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        try
        {
            base.OnPointerReleased(e);
        }
        finally
        {
            _altPressed = false;
        }
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (_altPressed && AltCommand is { } alt)
        {
            if (alt.CanExecute(CommandParameter))
                alt.Execute(CommandParameter);
            return;
        }

        base.OnClick();
    }

    private void OnIsCheckedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var value = e.GetNewValue<bool?>();
        UpdatePseudoClasses(value);
        (ControlAutomationPeer.FromElement(this) as PaneChipAutomationPeer)?.RaiseToggleStateChanged(
            e.GetOldValue<bool?>(), value);
    }

    private void UpdatePseudoClasses(bool? value)
    {
        PseudoClasses.Set(":checked", value == true);
        PseudoClasses.Set(":indeterminate", value is null);
    }

    private void ClickFromAutomation() => OnClick();

    private static ToggleState StateOf(bool? value) => value switch
    {
        true => ToggleState.On,
        false => ToggleState.Off,
        null => ToggleState.Indeterminate,
    };

    private sealed class PaneChipAutomationPeer(PaneChip owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => StateOf(owner.IsChecked);

        public void Toggle()
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();
            owner.ClickFromAutomation();
        }

        public void RaiseToggleStateChanged(bool? oldValue, bool? newValue) =>
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, StateOf(oldValue), StateOf(newValue));

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    }
}
