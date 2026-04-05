namespace EncDotNet.S100.Portrayals;

public sealed class DisplayPlane
{
    public required string Id { get; init; }

    public int? Order { get; init; }

    public required Description Description { get; init; }
}
