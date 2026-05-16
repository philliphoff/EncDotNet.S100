using System.ComponentModel;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// An optional time interval associated with a loaded dataset.
/// </summary>
/// <remarks>
/// Used for time-varying products such as S-104 water levels and
/// S-111 surface currents. Static products (S-102) report no time range.
/// </remarks>
/// <param name="Start">Inclusive start of the dataset's declared time range (UTC).</param>
/// <param name="End">Inclusive end of the dataset's declared time range (UTC).</param>
public readonly record struct TimeRange(
    [property: Description("Inclusive start of the dataset's declared time range, UTC ISO-8601.")] DateTimeOffset Start,
    [property: Description("Inclusive end of the dataset's declared time range, UTC ISO-8601.")] DateTimeOffset End)
{
    /// <summary>Returns <c>true</c> when the range contains <paramref name="instant"/> (inclusive).</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant <= End;
}
