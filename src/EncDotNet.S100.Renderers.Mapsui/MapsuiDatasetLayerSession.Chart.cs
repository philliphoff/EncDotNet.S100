using EncDotNet.S100.Rendering.Engine;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

// Chart tiles and one chart layer (map rendering slice 8). The projected
// stack's tiled vector layers no longer go on the map one by one: each run of
// consecutive tiled layers becomes one ChartLayer, which draws one chart tile
// per slot over the run's layers. A layer's display state (enabled, opacity,
// scale window), its overlap-suppression clip and its S-98 suppressed
// features become settings of its entry in the chart stack. Layers the chart
// can't hold (gridded coverage such as S-102, layers drawn live, the
// single-surface scene mode) stay map layers of their own and split the run,
// so the S-98 order holds across them.
public sealed partial class MapsuiDatasetLayerSession
{
    private readonly ChartSession _chartSession;

    // The chart layers on the map, one per run, reused by position so a run
    // keeps its tiles across composes. Under _sync.
    private readonly List<ChartLayer> _chartLayers = [];

    /// <summary>The session's chart session (a test seam).</summary>
    internal ChartSession ChartSession => _chartSession;

    /// <summary>The chart layers on the map, bottom first (a test seam).</summary>
    internal IReadOnlyList<ChartLayer> ChartLayersForTest
    {
        get { lock (_sync) return _chartLayers.ToArray(); }
    }

    /// <summary>
    /// Whether <paramref name="layer"/> is drawn into a chart layer rather than
    /// on the map by itself: a tiled vector layer whose scene isn't drawn live.
    /// </summary>
    private static bool JoinsChart(ILayer layer) =>
        layer is BaseLayer { CustomLayerRendererName: S100VectorTileRenderer.RendererName }
        && S100VectorTileRenderer.CanJoinChart(layer);

    /// <summary>
    /// The map layers of a projected stack, bottom first: one chart layer per
    /// run of consecutive tiled layers, published with this version's chart
    /// stack, and every other layer as it is. Call under <c>_sync</c> on the
    /// map thread, after the display state, scale windows and overlap clips
    /// are applied to the projected layers.
    /// </summary>
    private ILayer[] BuildMapLayers(long version, IReadOnlyList<MapsuiProjectedDatasetLayer> projected)
    {
        var layers = new List<ILayer>(projected.Count);
        var run = new List<ChartStackEntry>();
        var runs = 0;

        void Flush()
        {
            if (run.Count == 0)
            {
                return;
            }

            if (_chartLayers.Count <= runs)
            {
                _chartLayers.Add(new ChartLayer());
            }

            var chart = _chartLayers[runs++];
            var stack = new ChartStack(version, [.. run]);
            chart.Stack = stack;
            _chartSession.Publish(chart, stack);
            layers.Add(chart);
            run.Clear();
        }

        foreach (var item in projected)
        {
            var layer = item.Layer;
            if (JoinsChart(layer))
            {
                // A hidden layer keeps its place in the stack, so toggling it
                // re-rasters only the slots it touches.
                {
                    var window = layer as BaseLayer;
                    run.Add(new ChartStackEntry(layer)
                    {
                        Visible = layer.Enabled,
                        Opacity = layer.Opacity,
                        MinVisibleResolution = window?.MinVisible ?? 0,
                        MaxVisibleResolution = window?.MaxVisible ?? double.MaxValue,
                        SuppressedFeatures = item.SuppressedFeatures,
                        Finer = CoverageClip.Get(layer),
                    });
                }

                continue;
            }

            Flush();
            if (layer is BaseLayer { CustomLayerRendererName: S100VectorSceneRenderer.RendererName })
            {
                S100VectorSceneRenderer.SetSuppressedFeatures(layer, item.SuppressedFeatures);
            }

            layers.Add(layer);
        }

        Flush();

        // Runs that went away leave the map; their chart layers go too.
        for (var i = _chartLayers.Count - 1; i >= runs; i--)
        {
            _chartSession.Remove(_chartLayers[i]);
            _chartLayers.RemoveAt(i);
        }

        return [.. layers];
    }

    /// <summary>
    /// Builds, off the map thread, the suppression-filtered planes the chart
    /// stack of <paramref name="projected"/> will draw, so applying it on the
    /// map thread finds them ready.
    /// </summary>
    private static void PrepareChartSuppression(IReadOnlyList<MapsuiProjectedDatasetLayer> projected)
    {
        foreach (var item in projected)
        {
            if (item.SuppressedFeatures is { Count: > 0 } suppressed && JoinsChart(item.Layer))
            {
                S100VectorTileRenderer.PrepareSuppressedFeatures(item.Layer, suppressed);
            }
        }
    }

    // Under _sync, on dispose.
    private void RemoveChartLayers()
    {
        foreach (var chart in _chartLayers)
        {
            _chartSession.Remove(chart);
        }

        _chartLayers.Clear();
    }
}
