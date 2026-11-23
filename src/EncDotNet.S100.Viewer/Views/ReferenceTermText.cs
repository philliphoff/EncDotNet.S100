using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Text whose names are terms, such as a case in Drawn with: in "Colour is
/// red", "Colour" and "red" are <see cref="ReferenceTerm"/>s and "is" plain
/// words (symbol dictionary design note §4.6, R8, #1651).
/// </summary>
internal sealed class ReferenceTermText : TextBlock
{
    /// <summary>The text and its terms.</summary>
    public static readonly StyledProperty<TermText?> TermsProperty =
        AvaloniaProperty.Register<ReferenceTermText, TermText?>(nameof(Terms));

    /// <summary>How much of an inline term's height is taken back from its line, in logical pixels.</summary>
    internal static double InlineTrim { get; set; } = 4;

    static ReferenceTermText()
    {
        TermsProperty.Changed.AddClassHandler<ReferenceTermText>((t, _) => t.Build());
    }

    /// <inheritdoc cref="TermsProperty" />
    public TermText? Terms
    {
        get => GetValue(TermsProperty);
        set => SetValue(TermsProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    private void Build()
    {
        var inlines = new InlineCollection();
        var text = Terms;
        AutomationProperties.SetName(this, text?.Text);
        if (text is not null)
        {
            foreach (var run in text.Runs)
            {
                if (run.Key is { } key)
                {
                    var term = new ReferenceTerm
                    {
                        Text = run.Text,
                        Key = key,
                        Value = run.Value,
                        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                        // An embedded control sits on the text's bottom and adds its
                        // height to the line; taking back the line gap keeps lines
                        // with terms as tall as lines without.
                        Margin = new Avalonia.Thickness(0, -InlineTrim, 0, 0),
                    };
                    AutomationProperties.SetAutomationId(term, "Reference.Term");
                    inlines.Add(new InlineUIContainer(term) { BaselineAlignment = Avalonia.Media.BaselineAlignment.TextBottom });
                }
                else
                {
                    inlines.Add(new Run(run.Text));
                }
            }
        }

        Inlines = inlines;
    }
}
