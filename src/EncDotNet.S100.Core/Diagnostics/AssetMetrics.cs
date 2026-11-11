using System.Diagnostics.Metrics;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Asset source I/O metrics emitted by <see cref="EncDotNet.S100.Core.FileSystemAssetSource"/>
/// and <see cref="EncDotNet.S100.Core.ZipAssetSource"/>.
/// </summary>
internal static class AssetMetrics
{
    public static readonly Histogram<double> ReadDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.asset.read.duration",
            unit: "ms",
            description: "Time taken to open an asset from a file-system or ZIP source.");

    public static readonly Counter<long> BytesRead =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.asset.bytes.read.count",
            unit: "By",
            description: "Bytes read from asset sources (when stream length is known).");
}
