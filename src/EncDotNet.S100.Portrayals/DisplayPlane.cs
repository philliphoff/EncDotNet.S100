namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An S-100 Part 9 display plane declared by the catalogue (e.g.
/// <c>UnderRadar</c>, <c>OverRadar</c>): a drawing layer relative to a radar
/// overlay. Drawing instructions carry their plane as
/// <see cref="EncDotNet.S100.Pipelines.Vector.DisplayPlane"/>; this type is
/// the catalogue's declaration of it.
/// </summary>
public sealed class DisplayPlane
{
    /// <summary>Display-plane identifier, from <c>displayPlane/@id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Drawing order of the plane relative to the others, from
    /// <c>displayPlane/@order</c> (e.g. <c>-1</c> under radar, <c>1</c> over), or
    /// <see langword="null"/> when the attribute is absent or not an integer.
    /// </summary>
    public int? Order { get; init; }

    /// <summary>The display plane's <c>description</c> block.</summary>
    public required Description Description { get; init; }
}
