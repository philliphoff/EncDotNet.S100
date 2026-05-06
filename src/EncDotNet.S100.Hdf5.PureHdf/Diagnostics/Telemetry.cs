using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Hdf5.PureHdf.Diagnostics;

/// <summary>
/// Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Hdf5.PureHdf</c>.
/// </summary>
/// <remarks>
/// Exposes the metrics catalogued in the observability plan:
/// <list type="bullet">
/// <item><c>s100.hdf5.read.duration</c> — histogram of attribute / dataset read durations in milliseconds.</item>
/// <item><c>s100.hdf5.read.bytes</c> — counter of bytes returned from dataset reads (estimated from element count × <c>sizeof(T)</c>).</item>
/// </list>
/// </remarks>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Histogram<double> ReadDuration =
        Meter.CreateHistogram<double>(
            name: "s100.hdf5.read.duration",
            unit: "ms",
            description: "Time taken to read an HDF5 attribute, group listing, or dataset.");

    public static readonly Counter<long> ReadBytes =
        Meter.CreateCounter<long>(
            name: "s100.hdf5.read.bytes",
            unit: "By",
            description: "Bytes returned by HDF5 dataset reads.");
}
