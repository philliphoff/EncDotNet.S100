namespace EncDotNet.S100.Portrayals;

public sealed class ViewingGroupLayer
{
    public required string Id { get; init; }

    public required Description Description { get; init; }

    public IReadOnlyList<string> ViewingGroupIds { get; init; } = [];
}
