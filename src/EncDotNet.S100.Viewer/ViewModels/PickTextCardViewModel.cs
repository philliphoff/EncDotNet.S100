using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A long-text card in Object Information's "Text" section (#932): text
/// lifted out of the attribute table so it reads as a block rather than a
/// truncated cell. Two kinds share the <see cref="LongTextCardViewModel"/>
/// body:
/// <list type="bullet">
/// <item>Referenced text: a resolved <c>fileReference</c> (S-101 FC; aliases
/// <c>TXTDSC</c> / <c>NTXTDS</c>) whose
/// <see cref="PickAttribute.ExternalText"/> is the content of a file in the
/// dataset's exchange set. The header is the file's first non-empty line and
/// its name.</item>
/// <item>Long value: any other text value with a line break or more than
/// <see cref="LongValueThreshold"/> characters, such as S-122 / S-127
/// regulation text. The header is the attribute's display name.</item>
/// </list>
/// </summary>
/// <remarks>
/// The text is set proportional, because these are mostly prose (ECDIS
/// "textual description" files, regulations) rather than telex messages
/// whose columns line up by character. A text laid out in columns with runs
/// of spaces is set monospaced, so its columns still line up (see
/// <see cref="LooksColumnar"/>). Tab-separated tables, such as UKHO tidal
/// stream files, align at tab stops in either font.
/// </remarks>
internal sealed class PickTextCardViewModel : LongTextCardViewModel
{
    /// <summary>
    /// Text values longer than this lift out of the attribute table. About
    /// two lines of the value column at the panel's default width, so a value
    /// that would already wrap into a paragraph reads as one.
    /// </summary>
    internal const int LongValueThreshold = 120;

    private readonly string _body;
    private readonly string _copyText;
    private readonly PickAttribute _attribute;

    private PickTextCardViewModel(PickAttribute attribute, string title, string? fileName, string body, string copyText)
    {
        _attribute = attribute;
        Title = title;
        FileName = fileName;
        _body = body;
        _copyText = copyText;
        IsMonospace = LooksColumnar(body);
    }

    /// <summary>The card's heading: the file's first line, or the attribute's name.</summary>
    public string Title { get; }

    /// <summary>The referenced file's name (e.g. <c>101GB00N00549.TXT</c>); <see langword="null"/> for a long value.</summary>
    public string? FileName { get; }

    /// <summary>Whether <see cref="FileName"/> has a value.</summary>
    public bool HasFileName => FileName is not null;

    /// <summary>Whether the card shows a referenced file rather than a long value.</summary>
    public bool IsReferencedText => FileName is not null;

    /// <summary>The text below the heading; empty when a referenced file is a single line.</summary>
    public string Body => _body;

    /// <inheritdoc />
    protected override string? BodyText => _body;

    /// <summary>The whole text copied: a referenced file with its heading.</summary>
    public override string? CopyText => _copyText;

    /// <inheritdoc />
    public override string CopyTooltip => Strings.Tooltip_CopyTextCard;

    /// <inheritdoc />
    public override bool IsMonospace { get; }

    /// <inheritdoc />
    public override string? NoTextMessage => null;

    /// <inheritdoc />
    public override PickAttribute? ExaminerAttribute => _attribute;

    /// <summary>
    /// Builds a referenced-text card from a resolved file-reference
    /// attribute: <see cref="PickAttribute.RawValue"/> is the file name,
    /// <see cref="PickAttribute.ExternalText"/> the content.
    /// </summary>
    public static PickTextCardViewModel FromReferencedText(PickAttribute attribute, bool notForNavigation = false)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        var text = attribute.ExternalText ?? string.Empty;
        var (title, body) = SplitHeadingAndBody(text);

        // Fall back to the attribute's display name when the file is empty
        // so the card still carries a meaningful heading.
        if (string.IsNullOrEmpty(title))
            title = attribute.DisplayName;

        return new PickTextCardViewModel(attribute, title, attribute.RawValue, body, text)
        {
            NotForNavigation = notForNavigation,
        };
    }

    /// <summary>
    /// Builds a card for a long attribute value, headed by the attribute's
    /// name, after its complex parent's when it has one ("Information · Text").
    /// </summary>
    public static PickTextCardViewModel FromLongValue(PickAttribute attribute, PickAttribute? parent = null, bool notForNavigation = false)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        var title = parent is null ? attribute.DisplayName : parent.DisplayName + " · " + attribute.DisplayName;
        var text = ValueOf(attribute).Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        return new PickTextCardViewModel(attribute, title, null, text, text)
        {
            NotForNavigation = notForNavigation,
        };
    }

    /// <summary>
    /// Whether <paramref name="attribute"/> is a text value long enough to
    /// lift into a card: a leaf with no typed value (date, depth), whose
    /// shown value has a line break or more than
    /// <see cref="LongValueThreshold"/> characters.
    /// </summary>
    internal static bool IsLongValue(PickAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        if (attribute.Children.Count > 0
            || attribute.DateTimeValue is not null
            || attribute.DateTimeRangeValue is not null
            || attribute.DepthMetresValue is not null)
            return false;

        var value = ValueOf(attribute).Trim();
        return value.Length > LongValueThreshold || value.Contains('\n') || value.Contains('\r');
    }

    /// <summary>
    /// Whether <paramref name="text"/> is laid out in columns padded with
    /// spaces: at least two lines with a run of two or more spaces between
    /// words. One such line is more likely a typing slip than a table.
    /// </summary>
    internal static bool LooksColumnar(string text)
    {
        var columnLines = 0;
        foreach (var line in Lines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("  ", StringComparison.Ordinal) && ++columnLines >= 2)
                return true;
        }
        return false;
    }

    private static string ValueOf(PickAttribute attribute) => attribute.DisplayValue ?? attribute.RawValue;

    /// <summary>
    /// Splits referenced text into its first non-empty line (the heading)
    /// and the remaining body. Leading and trailing blank lines around each
    /// part are trimmed; interior blank lines (paragraph breaks) are kept.
    /// </summary>
    internal static (string Title, string Body) SplitHeadingAndBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, string.Empty);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var index = 0;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
            index++;

        if (index >= lines.Length)
            return (string.Empty, string.Empty);

        var title = lines[index].Trim();
        var body = string.Join('\n', lines, index + 1, lines.Length - index - 1).Trim();
        return (title, body);
    }
}
