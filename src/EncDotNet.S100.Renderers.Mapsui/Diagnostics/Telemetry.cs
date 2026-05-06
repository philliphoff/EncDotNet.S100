using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Renderers.Mapsui</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));
}
