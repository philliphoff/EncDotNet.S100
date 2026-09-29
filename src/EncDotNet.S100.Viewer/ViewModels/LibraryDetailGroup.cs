namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A titled group of fields in the Library details pane (Product, Coverage, Source).</summary>
/// <param name="Title">The group's header.</param>
/// <param name="Fields">The group's fields, in display order.</param>
internal sealed record LibraryDetailGroup(string Title, IReadOnlyList<LibraryDetailField> Fields)
{
    /// <summary>The header as shown: small upper-case.</summary>
    public string Header => Title.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>One label/value row in the Library details pane.</summary>
/// <param name="Label">The field label.</param>
/// <param name="Value">The value as shown (a download URL is shortened to host and file).</param>
/// <param name="IsMono">True for values read character by character: coordinates, editions, dates, paths, URLs.</param>
/// <param name="CopyValue">What clicking the value copies (the full URL or path), when it differs from nothing.</param>
internal sealed record LibraryDetailField(string Label, string Value, bool IsMono = false, string? CopyValue = null)
{
    public bool IsCopyable => CopyValue is not null;
}
