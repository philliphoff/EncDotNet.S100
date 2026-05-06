using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/>
/// for <c>EncDotNet.S100.Core</c>. Other libraries follow the same
/// pattern (a static <c>Telemetry</c> class in their own namespace).
/// </summary>
/// <remarks>
/// Held as <see langword="static"/> singletons so callers do not need
/// to thread the source / meter through DI. Both types are thread-safe
/// and inert when no listener is attached.
/// </remarks>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));
}
