namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An S-100 Part 9 viewing-group layer: a named collection of
/// <see cref="ViewingGroup"/>s that <see cref="DisplayMode"/>s reference to
/// build up the content shown in each mode.
/// </summary>
public sealed class ViewingGroupLayer
{
    /// <summary>Layer identifier, from <c>viewingGroupLayer/@id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The layer's <c>description</c> block.</summary>
    public required Description Description { get; init; }

    /// <summary>
    /// Ids of the member <see cref="ViewingGroup"/>s, from the text of each
    /// child <c>viewingGroup</c> element (trimmed; empty entries dropped).
    /// Empty when the layer lists none.
    /// </summary>
    public IReadOnlyList<string> ViewingGroupIds { get; init; } = [];
}
