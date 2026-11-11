namespace EncDotNet.S100.Portrayals;

/// <summary>
/// The human-readable <c>description</c> block attached to a portrayal
/// catalogue item (viewing group, display mode, context parameter, rule
/// file, asset, …).
/// </summary>
public sealed class Description
{
    /// <summary>
    /// Display name, from <c>description/name</c>. Empty when the item has no
    /// <c>description</c> element or the element has no <c>name</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Longer explanatory text, from the nested <c>description/description</c>
    /// element, or <see langword="null"/> when absent.
    /// </summary>
    public string? DescriptionText { get; init; }

    /// <summary>
    /// Language of <see cref="Name"/> and <see cref="DescriptionText"/>, from
    /// <c>description/language</c> (an ISO 639 code such as <c>eng</c>
    /// or <c>en</c>), or <see langword="null"/> when absent.
    /// </summary>
    public string? Language { get; init; }
}
