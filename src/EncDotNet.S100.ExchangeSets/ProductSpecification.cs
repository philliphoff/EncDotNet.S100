namespace EncDotNet.S100.ExchangeSets;

public sealed class ProductSpecification
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public DateOnly? Date { get; init; }

    public string? ProductIdentifier { get; init; }

    public int? Number { get; init; }

    public CompliancyCategory? CompliancyCategory { get; init; }
}
