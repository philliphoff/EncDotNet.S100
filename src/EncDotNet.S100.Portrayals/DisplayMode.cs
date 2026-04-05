namespace EncDotNet.S100.Portrayals;

public sealed class DisplayMode
{
    public required string Id { get; init; }

    public required Description Description { get; init; }

    public IReadOnlyList<string> ViewingGroupLayerIds { get; init; } = [];
}
