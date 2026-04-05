namespace EncDotNet.S100.Features;

public sealed class SimpleAttribute
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public string? Alias { get; init; }

    public string? Remarks { get; init; }

    public required string ValueType { get; init; }

    public IReadOnlyList<ListedValue> ListedValues { get; init; } = [];
}
