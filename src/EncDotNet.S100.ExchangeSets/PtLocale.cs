namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents a locale for a dataset, including language, country, and character encoding.
/// </summary>
public sealed class PtLocale
{
    public required string Language { get; init; }

    public string? Country { get; init; }

    public required string CharacterEncoding { get; init; }
}
