namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An S-100 Part 9 viewing group: a named set of drawing instructions that
/// the mariner can show or hide as a unit. Rules tag each drawing instruction
/// with a viewing-group id; <see cref="ViewingGroupLayer"/>s bundle groups and
/// <see cref="DisplayMode"/>s bundle layers.
/// </summary>
public sealed class ViewingGroup
{
    /// <summary>
    /// Viewing-group identifier, from <c>viewingGroup/@id</c>. Usually numeric
    /// (e.g. <c>"12200"</c>), matching the id carried by drawing instructions.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The viewing group's <c>description</c> block.</summary>
    public required Description Description { get; init; }
}
