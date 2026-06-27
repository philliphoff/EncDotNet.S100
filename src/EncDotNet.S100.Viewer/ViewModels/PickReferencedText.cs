using System;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A single externally referenced text block surfaced in the Object
/// Information panel's "Referenced text" section. Built from a resolved
/// <c>fileReference</c> attribute (S-101 Feature Catalogue, aliases
/// <c>TXTDSC</c> / <c>NTXTDS</c>): the attribute names an external text
/// file co-located in the dataset's exchange set, and
/// <see cref="PickAttribute.ExternalText"/> carries the resolved content.
/// </summary>
/// <remarks>
/// The card promotes the file's first non-empty line to a
/// <see cref="Title"/> heading and shows the remainder as <see cref="Body"/>,
/// mirroring how these support files are authored (a short headline followed
/// by the descriptive text). The full untouched content is preserved in
/// <see cref="ClipboardText"/> so a copy action round-trips the original file.
/// </remarks>
internal sealed class PickReferencedText
{
    /// <summary>Heading shown at the top of the card (the file's first non-empty line).</summary>
    public required string Title { get; init; }

    /// <summary>The name of the referenced external text file (e.g. <c>101GB00N00549.TXT</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>The referenced text below the heading; empty when the file is a single line.</summary>
    public required string Body { get; init; }

    /// <summary><c>true</c> when <see cref="Body"/> has displayable content.</summary>
    public bool HasBody => !string.IsNullOrEmpty(Body);

    /// <summary>The full, untouched referenced text used for the copy action.</summary>
    public required string ClipboardText { get; init; }

    /// <summary>
    /// Builds a <see cref="PickReferencedText"/> from a resolved
    /// file-reference attribute. The attribute's
    /// <see cref="PickAttribute.RawValue"/> supplies the file name and its
    /// <see cref="PickAttribute.ExternalText"/> supplies the content.
    /// </summary>
    public static PickReferencedText FromAttribute(PickAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        var text = attribute.ExternalText ?? string.Empty;
        var (title, body) = SplitHeadingAndBody(text);

        // Fall back to the attribute's display name when the file is empty
        // so the card still carries a meaningful heading.
        if (string.IsNullOrEmpty(title))
            title = attribute.DisplayName;

        return new PickReferencedText
        {
            Title = title,
            FileName = attribute.RawValue,
            Body = body,
            ClipboardText = text,
        };
    }

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
