using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The AML tag (decision 2): "AML · {code}" in outlined ink with a layers
/// glyph, so it never reads as a state colour or as a neutral tag. The full
/// product name and version go in its tooltip. Styled in <c>AmlChips.axaml</c>.
/// </summary>
public class AmlTagChip : TemplatedControl
{
    /// <summary>The tag text, e.g. "AML · CLB" or "AML 3.0".</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AmlTagChip, string?>(nameof(Text));

    /// <inheritdoc cref="TextProperty"/>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>
/// The protective-marking chip (decision 3): the marking exactly as the data
/// gives it, upper case and never shortened. UNCLASSIFIED is outlined and
/// muted; anything above it is solid ink (the <c>:elevated</c> pseudo-class).
/// Never red: a marking is a handling rule, not a fault. Styled in
/// <c>AmlChips.axaml</c>.
/// </summary>
public class AmlMarkingChip : TemplatedControl
{
    /// <summary>The marking, e.g. "UNCLASSIFIED" or "NATO RESTRICTED +1".</summary>
    public static readonly StyledProperty<string?> MarkingProperty =
        AvaloniaProperty.Register<AmlMarkingChip, string?>(nameof(Marking));

    /// <summary>The marking as shown: normalized to upper case.</summary>
    public static readonly DirectProperty<AmlMarkingChip, string?> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<AmlMarkingChip, string?>(nameof(DisplayText), o => o.DisplayText);

    private string? _displayText;

    static AmlMarkingChip()
    {
        MarkingProperty.Changed.AddClassHandler<AmlMarkingChip>((chip, _) => chip.Update());
    }

    /// <inheritdoc cref="MarkingProperty"/>
    public string? Marking
    {
        get => GetValue(MarkingProperty);
        set => SetValue(MarkingProperty, value);
    }

    /// <inheritdoc cref="DisplayTextProperty"/>
    public string? DisplayText
    {
        get => _displayText;
        private set => SetAndRaise(DisplayTextProperty, ref _displayText, value);
    }

    /// <summary>Whether a marking ranks above UNCLASSIFIED, drawn as solid ink.</summary>
    /// <param name="marking">The marking, possibly with a "+N" tie suffix.</param>
    public static bool IsElevated(string? marking)
    {
        var text = AmlProtectiveMarking.Normalize(marking);
        if (text is null)
            return false;

        // "NATO RESTRICTED +1" ranks by its marking, not its suffix.
        var plus = text.LastIndexOf(" +", StringComparison.Ordinal);
        if (plus > 0 && int.TryParse(text.AsSpan(plus + 2), out _))
            text = text[..plus];
        return AmlProtectiveMarking.IsAboveUnclassified(text);
    }

    private void Update()
    {
        DisplayText = AmlProtectiveMarking.Normalize(Marking);
        PseudoClasses.Set(":elevated", IsElevated(Marking));
    }
}
