namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An S-100 Part 9 §11.7 display mode (e.g. <c>DisplayBase</c>,
/// <c>StandardDisplay</c>, <c>OtherInformation</c>): a named selection of
/// <see cref="ViewingGroupLayer"/>s whose viewing groups are visible when the
/// mode is active. <see cref="DisplayModeMembership"/> resolves a mode to its
/// viewing-group ids.
/// </summary>
public sealed class DisplayMode
{
    /// <summary>Display-mode identifier, from <c>displayMode/@id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The display mode's <c>description</c> block.</summary>
    public required Description Description { get; init; }

    /// <summary>
    /// Ids of the <see cref="ViewingGroupLayer"/>s included in this mode, from
    /// the text of each child <c>viewingGroupLayer</c> element (trimmed; empty
    /// entries dropped). Empty when the mode lists none.
    /// </summary>
    public IReadOnlyList<string> ViewingGroupLayerIds { get; init; } = [];
}
