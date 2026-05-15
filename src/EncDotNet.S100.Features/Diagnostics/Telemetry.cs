using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Features.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Features</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    /// <summary>
    /// Cache-hit counter for <see cref="FeatureCatalogueManager.GetCatalogue(EncDotNet.S100.Core.SpecRef)"/>.
    /// Tagged with <see cref="TelemetryTags.Product"/>.
    /// </summary>
    public static readonly Counter<long> FeatureCatalogueCacheHit =
        Meter.CreateCounter<long>(
            "s100.featurecatalogue.cache.hit.count",
            unit: "{events}",
            description: "Reuses of an already-parsed feature catalogue in FeatureCatalogueManager. Tagged with s100.product.");

    /// <summary>
    /// Cache-miss counter for <see cref="FeatureCatalogueManager.GetCatalogue(EncDotNet.S100.Core.SpecRef)"/>.
    /// Fires once per spec the first time the catalogue is parsed.
    /// </summary>
    public static readonly Counter<long> FeatureCatalogueCacheMiss =
        Meter.CreateCounter<long>(
            "s100.featurecatalogue.cache.miss.count",
            unit: "{events}",
            description: "First-time parses of a feature catalogue in FeatureCatalogueManager. Tagged with s100.product.");
}

internal static class FeatureCatalogueCacheMetrics
{
    public static void RecordHit(string product)
    {
        Telemetry.FeatureCatalogueCacheHit.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product));
    }

    public static void RecordMiss(string product)
    {
        Telemetry.FeatureCatalogueCacheMiss.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product));
    }
}
