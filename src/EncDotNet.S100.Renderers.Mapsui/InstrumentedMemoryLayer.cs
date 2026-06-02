using System.Collections.Generic;
using System.Diagnostics;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// <see cref="MemoryLayer"/> subclass that records per-frame timing and
/// selectivity metrics each time Mapsui asks for the layer's visible
/// features.  Used by <see cref="MapsuiDisplayListRenderer"/> so we can
/// quantify how much of a render frame is spent in the layer's
/// extent-filter loop versus downstream Skia drawing — i.e. whether a
/// spatial index (e.g. an STRtree-backed provider) would actually help
/// real-world S-100 datasets.
/// </summary>
/// <remarks>
/// Mapsui's base <see cref="MemoryLayer"/> implements <c>GetFeatures</c>
/// as an O(N) linear extent test on every render frame.  This subclass
/// re-implements the same filter while wrapping it in a
/// <see cref="Stopwatch"/> measurement and emitting:
/// <list type="bullet">
///   <item><description><c>s100.layer.getfeatures.duration</c> (ms)</description></item>
///   <item><description><c>s100.layer.getfeatures.visible.count</c> (K)</description></item>
///   <item><description><c>s100.layer.getfeatures.total.count</c> (N)</description></item>
/// </list>
/// Each metric is tagged with <c>s100.product</c> when the renderer is
/// configured by a dataset processor, so per-product percentiles can be
/// read directly from the OTLP / console exporter.
/// </remarks>
public sealed class InstrumentedMemoryLayer : MemoryLayer
{
    /// <summary>
    /// Inter-frame gaps longer than this are treated as idle (no
    /// rendering happening) and excluded from the
    /// <c>s100.layer.frame.interval</c> /
    /// <c>s100.layer.frame.render_plus_paint</c> histograms. Without
    /// this, a single idle pause would dominate the percentile.
    /// </summary>
    private const double IdleGapThresholdMs = 500.0;

    private readonly KeyValuePair<string, object?>[] _tags;
    private readonly string? _product;
    private long _lastExitTimestamp;

    /// <summary>
    /// Creates a new instrumented layer.  <paramref name="product"/> is
    /// attached as the <c>s100.product</c> dimension on each emitted
    /// metric; pass <see langword="null"/> for ad-hoc / one-shot use.
    /// </summary>
    public InstrumentedMemoryLayer(string? product = null)
    {
        _product = product;
        _tags = product is null
            ? System.Array.Empty<KeyValuePair<string, object?>>()
            : new[] { new KeyValuePair<string, object?>("s100.product", product) };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Replicates <see cref="MemoryLayer.GetFeatures"/>'s extent-filter
    /// behaviour (Mapsui grows the query rect by
    /// <c>SymbolStyle.DefaultWidth * 2 * resolution</c> to keep large
    /// symbols on edge features visible).  Returns a materialised list
    /// rather than yielding lazily so the recorded duration reflects
    /// the filter pass alone, not downstream consumer work.
    /// </remarks>
    public override IEnumerable<IFeature> GetFeatures(MRect? rect, double resolution)
    {
        if (rect is null)
            return System.Array.Empty<IFeature>();

        // Inter-frame gap: time since the previous GetFeatures returned.
        // Captured before doing any filter work, so it reflects the gap
        // between two consecutive frame-emissions on this layer. Mapsui
        // calls GetFeatures once per visible layer per render pass, so
        // for a single-layer map this is the frame interval; the
        // render+paint slice is the gap minus the previous frame's
        // filter duration. Skipped when the gap exceeds the idle
        // threshold (no active rendering between samples).
        var entryTimestamp = Stopwatch.GetTimestamp();
        if (_lastExitTimestamp != 0)
        {
            var gapMs = Stopwatch.GetElapsedTime(_lastExitTimestamp, entryTimestamp).TotalMilliseconds;
            if (gapMs > 0 && gapMs <= IdleGapThresholdMs)
            {
                S100Diag.Telemetry.LayerFrameInterval.Record(gapMs, _tags);
            }
        }

        var startTimestamp = entryTimestamp;
        var grow = SymbolStyle.DefaultWidth * 2.0 * resolution;
        var biggerRect = rect.Grow(grow, grow);

        var visible = new List<IFeature>();
        long total = 0;
        foreach (var feature in Features)
        {
            total++;
            if (feature is not null && feature.Extent?.Intersects(biggerRect) == true)
                visible.Add(feature);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        S100Diag.Telemetry.LayerGetFeaturesDuration.Record(elapsedMs, _tags);
        S100Diag.Telemetry.LayerGetFeaturesVisibleCount.Record(visible.Count, _tags);
        S100Diag.Telemetry.LayerGetFeaturesTotalCount.Record(total, _tags);
        S100Diag.Telemetry.LayerGetFeaturesCalls.Add(1, _tags);
        S100Diag.Telemetry.RecordGetFeaturesCall(_product);

        _lastExitTimestamp = Stopwatch.GetTimestamp();
        return visible;
    }
}
