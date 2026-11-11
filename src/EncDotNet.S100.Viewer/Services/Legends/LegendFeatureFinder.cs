using System.Runtime.CompilerServices;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Tools;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>How a legend row's features were found (<see cref="LegendFeatureFind"/>).</summary>
internal enum LegendFindKind
{
    /// <summary>The row's features drawn in the view are ringed.</summary>
    Features,

    /// <summary>
    /// The row is a band of a colour or arrow ramp (S-102 depths, a weather
    /// field, S-111 speeds): its area isn't ringed yet (follow-up under #1402).
    /// </summary>
    Band,

    /// <summary>No drawn sub-layer of the section has the row.</summary>
    None,
}

/// <summary>
/// The features a legend row describes that are drawn in the map view
/// (legends design note D19, #1412): what hovering the row rings on the map.
/// </summary>
/// <param name="SectionId">The section the row is in.</param>
/// <param name="RowLabel">The row's label.</param>
/// <param name="Kind">How the row maps to the map.</param>
/// <param name="Rings">The geometry ringed, at most <see cref="LegendFeatureFinder.MaxRings"/> features.</param>
/// <param name="MatchCount">How many drawn features in view match, ringed or not.</param>
internal sealed record LegendFeatureFind(
    string SectionId,
    string RowLabel,
    LegendFindKind Kind,
    IReadOnlyList<PickHighlightGeometry> Rings,
    int MatchCount)
{
    /// <summary>Whether more features match than are ringed.</summary>
    public bool IsCapped => MatchCount > RingedCount;

    /// <summary>How many features are ringed (a multi-part area rings each part).</summary>
    public int RingedCount { get; init; }
}

/// <summary>
/// Maps a legend row back to the features it describes, from what the
/// portrayal pipeline already holds: the stack entries' display lists and
/// geometry, the sub-layer each row comes from, the row's kind and label and,
/// for a catalogue product, the symbol. Nothing is portrayed again.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A catalogue key row (S-101, S-57, D7) is a point symbol: the features
/// that draw that symbol in view at the view scale.</item>
/// <item>A vector layer's row (S-124, S-411, AML, TC, fronts, the S-102
/// safety contour) matches each drawn feature by the rule
/// <see cref="LegendService.FindRow"/> uses for a pick, so a feature's
/// <b>In legend</b> row and the row that rings it agree.</item>
/// <item>A station or isoline layer with one row rings what it draws.</item>
/// <item>A band of a colour or arrow ramp reports
/// <see cref="LegendFindKind.Band"/> and rings nothing yet.</item>
/// </list>
/// </remarks>
internal static class LegendFeatureFinder
{
    /// <summary>The most features one row rings; the rest are counted.</summary>
    public const int MaxRings = 100;

    private static readonly ConditionalWeakTable<IReadOnlyList<DrawingInstruction>, IReadOnlyList<DrawnFeature>> Drawn = new();
    private static readonly ConditionalWeakTable<IDatasetProcessor, IReadOnlyDictionary<string, FeatureSummary>> Summaries = new();

    /// <summary>Finds the features drawn in view that <paramref name="rowLabel"/> describes.</summary>
    /// <param name="section">The row's section.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <param name="groupKey">The row's sub-layer key, or null for the first group with that label.</param>
    /// <param name="stack">The layer stack, bottom first.</param>
    /// <param name="isVisible">Whether a dataset (session id) is drawn; a dataset hidden by the user isn't.</param>
    /// <param name="processorOf">The processor of a dataset (session id), for feature class names; null when not known.</param>
    /// <param name="view">The map view, or null to find everywhere.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    /// <param name="maxRings">The most features to ring.</param>
    public static LegendFeatureFind Find(
        LegendSection section,
        string rowLabel,
        string? groupKey,
        IReadOnlyList<LayerStackEntry> stack,
        Func<string, bool> isVisible,
        Func<string, IDatasetProcessor?> processorOf,
        GeoBounds? view,
        double scaleDenominator,
        int maxRings = MaxRings)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(rowLabel);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(isVisible);
        ArgumentNullException.ThrowIfNull(processorOf);

        var group = section.Groups.FirstOrDefault(g =>
            (groupKey is null || string.Equals(g.Key, groupKey, StringComparison.Ordinal))
            && g.Rows.Any(r => string.Equals(r.Label, rowLabel, StringComparison.Ordinal)));
        if (group is null)
            return new LegendFeatureFind(section.Id, rowLabel, LegendFindKind.None, [], 0);

        var collector = new Collector(maxRings);
        var kind = LegendFindKind.None;
        var datasets = section.DatasetIds.ToHashSet(StringComparer.Ordinal);

        // Top of the stack first, so the cap keeps what's drawn on top.
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var item = stack[i];
            if (!datasets.Contains(item.SourceDatasetId) || !isVisible(item.SourceDatasetId))
                continue;

            if (group.Key == LegendService.CatalogueGroupKey)
            {
                if (item.Item.Payload is VectorStackPayload catalogue && LegendService.IsCatalogueProduct(catalogue))
                {
                    kind = LegendFindKind.Features;
                    FindSymbols(catalogue, item.SuppressedFeatures, rowLabel, view, scaleDenominator, collector);
                }
                continue;
            }

            if (LegendService.LegendOf(item) is not { } sub
                || !string.Equals(sub.Key, group.Key, StringComparison.Ordinal)
                || !string.Equals(LegendService.SectionId(sub.Spec ?? section.Spec, sub.Key), section.Id, StringComparison.Ordinal)
                || !sub.Rows.Any(r => string.Equals(r.Label, rowLabel, StringComparison.Ordinal)))
            {
                continue;
            }

            switch (item.Item.Payload)
            {
                case VectorStackPayload vector:
                    kind = LegendFindKind.Features;
                    FindDrawn(
                        vector.SubLayer.Instructions, vector.Result.GeometryProvider, sub.Rows, rowLabel,
                        item.SuppressedFeatures, processorOf(item.SourceDatasetId),
                        vector.SubLayer.ApplyOutOfBandCap ? vector.Result.OutOfBandMinDisplayScale : null,
                        view, scaleDenominator, collector);
                    break;
                case CoverageStackPayload { SubLayer: VectorCoverageSubLayer stations }:
                    kind = LegendFindKind.Features;
                    FindDrawn(
                        stations.Instructions, stations.GeometryProvider, sub.Rows, rowLabel,
                        item.SuppressedFeatures, processorOf(item.SourceDatasetId), null,
                        view, scaleDenominator, collector);
                    break;
                case CoverageStackPayload { SubLayer: GlyphCoverageSubLayer glyphs } when sub.Rows.Count == 1:
                    kind = LegendFindKind.Features;
                    foreach (var glyph in glyphs.Glyphs)
                    {
                        var (lon, lat) = SphericalMercator.ToLonLat(glyph.MercatorX, glyph.MercatorY);
                        var at = new GeoPosition(lat, lon);
                        if (view is not { } v || v.Contains(at))
                            collector.Add(Point(at));
                    }
                    break;
                case CoverageStackPayload { SubLayer: IsolineCoverageSubLayer isolines } when sub.Rows.Count == 1:
                    kind = LegendFindKind.Features;
                    foreach (var isoline in isolines.Isolines)
                    {
                        var line = FromMercator(isoline.MercatorCoordinates);
                        if (line.Count > 1 && (view is not { } v || GeoBounds.FromPositions(line) is { } b && v.Intersects(b)))
                            collector.Add(new PickHighlightGeometry([], [], [line], []));
                    }
                    break;
                case CoverageStackPayload { SubLayer: GridCoverageSubLayer or ArrowCoverageSubLayer }:
                    if (kind == LegendFindKind.None)
                        kind = LegendFindKind.Band;
                    break;
            }
        }

        return new LegendFeatureFind(section.Id, rowLabel, kind, collector.Rings, collector.Count) { RingedCount = collector.Ringed };
    }

    // ── Catalogue symbols ────────────────────────────────────────────────

    private static void FindSymbols(
        VectorStackPayload payload, IReadOnlySet<string>? suppressed, string rowLabel,
        GeoBounds? view, double scale, Collector collector)
    {
        var titles = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Matches(string name)
        {
            if (!titles.TryGetValue(name, out var match))
            {
                match = string.Equals(CatalogueSymbolKey.Label(name, payload.Result.SymbolTitleProvider?.Invoke(name)), rowLabel, StringComparison.Ordinal);
                titles.Add(name, match);
            }
            return match;
        }

        foreach (var symbol in CatalogueSymbolKey.Drawn(payload, suppressed, view, scale))
        {
            if (!Matches(symbol.Name))
                continue;
            if (symbol.At is { } at)
            {
                collector.Add(Point(at));
                continue;
            }
            collector.Add(payload.Result.GeometryProvider.GetGeometry(symbol.FeatureReference));
        }
    }

    // ── Vector rows ──────────────────────────────────────────────────────

    private static void FindDrawn(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider,
        IReadOnlyList<LegendEntry> rows,
        string rowLabel,
        IReadOnlySet<string>? suppressed,
        IDatasetProcessor? processor,
        int? outOfBandCap,
        GeoBounds? view,
        double scale,
        Collector collector)
    {
        // A layer of several rows names them by feature class, which the
        // processor's feature list knows; one row needs no name.
        var names = rows.Count > 1 && processor is not null ? SummariesOf(processor) : null;
        foreach (var drawn in Drawn.GetValue(instructions, BuildDrawn))
        {
            if (suppressed is not null && suppressed.Contains(drawn.FeatureReference))
                continue;
            if (!double.IsNaN(scale) && !drawn.IsDrawnAt(scale, outOfBandCap))
                continue;

            var geometry = geometryProvider.GetGeometry(drawn.FeatureReference);
            FeatureSummary? summary = null;
            names?.TryGetValue(drawn.FeatureReference, out summary);
            var facts = new LegendFeatureFacts(
                GeometryOf(geometry, drawn.Shape),
                summary?.FeatureType,
                summary?.FeatureTypeName);
            if (!string.Equals(LegendService.VectorRowLabel(rows, facts), rowLabel, StringComparison.Ordinal))
                continue;
            if (geometry is null)
                continue;
            if (view is { } v && (FootprintOf(geometry) is not { } box || !v.Intersects(box)))
                continue;
            collector.Add(geometry);
        }
    }

    private static S100GeometryType GeometryOf(FeatureGeometry? geometry, DrawnShape shape) => geometry?.Type switch
    {
        GeometryType.Point => S100GeometryType.Point,
        GeometryType.Curve => S100GeometryType.Curve,
        GeometryType.Surface => S100GeometryType.Surface,
        _ => shape.Geometry,
    };

    private static IReadOnlyDictionary<string, FeatureSummary> SummariesOf(IDatasetProcessor processor) =>
        Summaries.GetValue(processor, p =>
        {
            var map = new Dictionary<string, FeatureSummary>(StringComparer.Ordinal);
            try
            {
                foreach (var summary in p.EnumerateFeatures())
                    map.TryAdd(summary.FeatureRef, summary);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // A dataset being unloaded lists no names; its rows ring nothing.
            }
            return map;
        });

    private static IReadOnlyList<DrawnFeature> BuildDrawn(IReadOnlyList<DrawingInstruction> instructions)
    {
        var byRef = new Dictionary<string, DrawnFeature>(StringComparer.Ordinal);
        var order = new List<DrawnFeature>();
        foreach (var instruction in instructions)
        {
            if (string.IsNullOrEmpty(instruction.FeatureReference))
                continue;
            if (!byRef.TryGetValue(instruction.FeatureReference, out var drawn))
            {
                drawn = new DrawnFeature(instruction.FeatureReference);
                byRef.Add(instruction.FeatureReference, drawn);
                order.Add(drawn);
            }
            drawn.Add(instruction);
        }
        return order;
    }

    /// <summary>One feature of a display list: what it draws, and the scales it draws at.</summary>
    private sealed class DrawnFeature(string featureReference)
    {
        private bool _unboundedMin;
        private bool _unboundedMax;
        private double _min;
        private double _max = double.MaxValue;

        public string FeatureReference { get; } = featureReference;

        public DrawnShape Shape { get; private set; }

        public void Add(DrawingInstruction instruction)
        {
            Shape = Shape.With(instruction);
            // The widest SCAMIN window of its instructions: drawn when any is.
            if (instruction.ScaleMinimum is { } min)
                _min = Math.Max(_min, min);
            else
                _unboundedMin = true;
            if (instruction.ScaleMaximum is { } max)
                _max = Math.Min(_max, max);
            else
                _unboundedMax = true;
        }

        public bool IsDrawnAt(double scale, int? outOfBandCap)
        {
            var min = _unboundedMin ? double.MaxValue : _min;
            if (outOfBandCap is { } cap)
                min = Math.Min(min, cap);
            var max = _unboundedMax ? 0 : _max;
            return scale <= min && scale >= max;
        }
    }

    // ── Geometry ─────────────────────────────────────────────────────────

    private static PickHighlightGeometry Point(GeoPosition at) => new([], [], [], [at]);

    private static List<GeoPosition> FromMercator(double[] xy)
    {
        var line = new List<GeoPosition>(xy.Length / 2);
        for (var i = 0; i + 1 < xy.Length; i += 2)
        {
            var (lon, lat) = SphericalMercator.ToLonLat(xy[i], xy[i + 1]);
            line.Add(new GeoPosition(lat, lon));
        }
        return line;
    }

    private static GeoBounds? FootprintOf(FeatureGeometry geometry) =>
        GeoBounds.FromPositions(geometry.Parts.Count > 0 ? geometry.Parts.SelectMany(p => p) : geometry.Coordinates);

    /// <summary>A feature's geometry as the rings the overlay draws: one per surface part.</summary>
    internal static IEnumerable<PickHighlightGeometry> ToHighlight(FeatureGeometry geometry)
    {
        switch (geometry.Type)
        {
            case GeometryType.Point:
                var points = geometry.Parts.Count > 0 ? [.. geometry.Parts.SelectMany(p => p)] : geometry.Coordinates;
                if (points.Count > 0)
                    yield return new PickHighlightGeometry([], [], [], points);
                break;
            case GeometryType.Curve:
                var curves = geometry.Parts.Count > 0 ? geometry.Parts : [geometry.Coordinates];
                yield return new PickHighlightGeometry([], [], curves, []);
                break;
            case GeometryType.Surface:
                if (geometry.Parts.Count == 0)
                {
                    yield return new PickHighlightGeometry(geometry.Coordinates, geometry.InteriorRings, [], []);
                    break;
                }
                for (var i = 0; i < geometry.Parts.Count; i++)
                {
                    var holes = i < geometry.PartInteriorRings.Count ? geometry.PartInteriorRings[i] : [];
                    yield return new PickHighlightGeometry(geometry.Parts[i], holes, [], []);
                }
                break;
        }
    }

    /// <summary>Counts matches and keeps the first <c>max</c> features' rings.</summary>
    private sealed class Collector(int max)
    {
        public List<PickHighlightGeometry> Rings { get; } = [];

        public int Count { get; private set; }

        public int Ringed { get; private set; }

        public void Add(FeatureGeometry? geometry)
        {
            if (geometry is null)
                return;
            Count++;
            if (Ringed >= max)
                return;
            var any = false;
            foreach (var ring in ToHighlight(geometry))
            {
                if (!ring.HasGeometry)
                    continue;
                Rings.Add(ring);
                any = true;
            }
            if (any)
                Ringed++;
        }

        public void Add(PickHighlightGeometry ring)
        {
            Count++;
            if (Ringed >= max)
                return;
            Rings.Add(ring);
            Ringed++;
        }
    }
}

/// <summary>What a display list draws for one feature, which tells its geometry when the product doesn't report it.</summary>
internal readonly record struct DrawnShape(bool HasArea, bool HasLine)
{
    /// <summary>The shape with one more instruction drawn.</summary>
    public DrawnShape With(DrawingInstruction instruction) => instruction switch
    {
        AreaInstruction => this with { HasArea = true },
        LineInstruction => this with { HasLine = true },
        _ => this,
    };

    /// <summary>The shape of a feature's instructions.</summary>
    public static DrawnShape Of(IEnumerable<DrawingInstruction> instructions) =>
        instructions.Aggregate(default(DrawnShape), (shape, instruction) => shape.With(instruction));

    /// <summary>The geometry it implies: a fill is a surface, a lone line a curve, else a point.</summary>
    public S100GeometryType Geometry =>
        HasArea ? S100GeometryType.Surface
        : HasLine ? S100GeometryType.Curve
        : S100GeometryType.Point;
}

/// <summary>What a vector row is matched on: the feature's geometry and class (<see cref="LegendService.VectorRowLabel"/>).</summary>
/// <param name="Geometry">The feature's geometry.</param>
/// <param name="FeatureType">The feature class code, or null when not known.</param>
/// <param name="FeatureTypeName">The feature class name, or null when not known.</param>
internal readonly record struct LegendFeatureFacts(S100GeometryType Geometry, string? FeatureType, string? FeatureTypeName);
