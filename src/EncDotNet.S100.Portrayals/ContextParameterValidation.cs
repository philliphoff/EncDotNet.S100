namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Optional value constraints for a <see cref="ContextParameter"/>, from its
/// <c>validate</c> element. The reader captures these as declared; they are
/// not enforced by this library.
/// </summary>
public sealed class ContextParameterValidation
{
    /// <summary>
    /// XPath expression the value must satisfy, from <c>validate/xpath</c>, or
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? XPath { get; init; }

    /// <summary>
    /// Regular expression the value must match, from <c>validate/regex</c>, or
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? Regex { get; init; }

    /// <summary>
    /// Message to show when validation fails, from
    /// <c>validate/errorMessage/text</c>, or <see langword="null"/> when absent.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
