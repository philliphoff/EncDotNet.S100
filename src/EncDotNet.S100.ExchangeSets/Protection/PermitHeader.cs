namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The <c>header</c> section of a PERMIT.XML file: provenance and addressing
/// metadata that applies to the permits that follow it.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.4.2.
/// </remarks>
public sealed class PermitHeader
{
    /// <summary>The date the permit file was issued (§15-7.4.2).</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>The name of the Data Server that generated the permit file.</summary>
    public string? DataServerName { get; init; }

    /// <summary>The short identifier of the Data Server.</summary>
    public string? DataServerIdentifier { get; init; }

    /// <summary>The S-100 version the permit file conforms to (e.g. <c>1.0.0</c>).</summary>
    public string? Version { get; init; }

    /// <summary>
    /// The user permit the permits are addressed to, allowing a client to verify
    /// the file is intended for its system.
    /// </summary>
    public string? UserPermit { get; init; }
}
