using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Scripting.MoonSharp.Diagnostics;

/// <summary>
/// Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Scripting.MoonSharp</c>.
/// </summary>
/// <remarks>
/// Lua portrayal rules (S-100 Part 9A) fire at very high frequency. Per-call
/// activities are gated by listener subscription (the <see cref="ActivitySource"/>
/// is inert when no <c>ActivityListener</c> is attached) so the host can
/// disable trace volume without re-instrumenting the engine. The
/// <see cref="InvokeCount"/> counter and <see cref="InvokeDuration"/>
/// histogram run unconditionally — they remain inert until a
/// <c>MeterListener</c> subscribes.
/// </remarks>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Counter<long> InvokeCount =
        Meter.CreateCounter<long>(
            name: "s100.lua.rule.invoke.count",
            unit: "{calls}",
            description: "Number of Lua callable invocations (Call / CallMultiReturn).");

    public static readonly Histogram<double> InvokeDuration =
        Meter.CreateHistogram<double>(
            name: "s100.lua.rule.invoke.duration",
            unit: "ms",
            description: "Wall-clock duration of Lua callable invocations.");
}
