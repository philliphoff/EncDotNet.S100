namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// An optional time interval associated with a loaded dataset.
/// </summary>
/// <remarks>
/// Used for time-varying products such as S-104 water levels and
/// S-111 surface currents. Static products (S-102) report no time range.
/// </remarks>
public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>Returns <c>true</c> when the range contains <paramref name="instant"/> (inclusive).</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant <= End;
}
