using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using Feature = EncDotNet.S100.Pipelines.Vector.Feature;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Re-portrays the S-101 depth features inside a water level adjustment's
/// footprint with the water level added to their depths (S-98 Appendix D,
/// <c>docs/design/chart-wide-water-level-adjustment.md</c> §3.2 and §11).
/// </summary>
/// <remarks>
/// <para>
/// The rules see adjusted values through <see cref="S101DepthShift"/> and
/// compare them with the mariner's unchanged thresholds, so the shades, the
/// safety contour, the contour labels and the sounding figures all show the
/// adjusted depth. An area or curve gets the lowest water level over its
/// part inside the footprint, floored to 0.1 m, so the water is never
/// overstated; each sounding gets the water level at its own position.
/// </para>
/// <para>
/// A feature that straddles the footprint's edge is split there (§11 Q5):
/// the part inside is drawn adjusted and the part outside charted, as two
/// synthetic features whose geometry <see cref="Result.Geometry"/> supplies.
/// Each adjusted sounding gets a small marker after its figure (§11 Q3).
/// Only the depth features in the footprint are re-portrayed; everything
/// else keeps the charted instructions.
/// </para>
/// </remarks>
internal static class S101DepthAdjustmentPortrayal
{
    /// <summary>The feature types whose depths are adjusted.</summary>
    public static IReadOnlySet<string> DepthFeatureTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "DepthArea", "DredgedArea", "DepthContour", "Sounding", "UnderwaterAwashRock", "Wreck", "Obstruction",
    };

    /// <summary>The marker drawn after an adjusted sounding's figure.</summary>
    public const string SoundingMarker = "◆";

    /// <summary>The offset of synthetic ids for the part of a split feature inside the footprint.</summary>
    public const long InsideIdOffset = 1L << 40;

    /// <summary>The offset of synthetic ids for the part of a split feature outside the footprint.</summary>
    public const long OutsideIdOffset = 2L << 40;

    private const double Bucket = 0.1;
    private const int Lattice = 8;

    /// <summary>The adjusted display list.</summary>
    /// <param name="Instructions">The charted instructions with the depth features in the footprint replaced.</param>
    /// <param name="Geometry">The geometry of each synthetic (split) feature id.</param>
    /// <param name="SourceIds">The source feature of each synthetic id.</param>
    /// <param name="Heights">The water level applied to each adjusted area, curve or point feature.</param>
    /// <param name="AdjustedSoundings">The adjusted sounding feature ids.</param>
    public sealed record Result(
        IReadOnlyList<DrawingInstruction> Instructions,
        IReadOnlyDictionary<long, FeatureGeometry> Geometry,
        IReadOnlyDictionary<long, long> SourceIds,
        IReadOnlyDictionary<long, double> Heights,
        IReadOnlySet<long> AdjustedSoundings);

    /// <summary>Applies <paramref name="adjustment"/> to the charted display list.</summary>
    /// <param name="charted">The charted, unfiltered rule output.</param>
    /// <param name="features">The dataset's features by record id.</param>
    /// <param name="adjustment">The water level to add.</param>
    /// <param name="datumAt">The sounding datum code at a position (latitude, longitude), or <see langword="null"/> when undeclared.</param>
    /// <param name="openSession">Opens a Lua session whose rules read depths with a shift added.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The adjusted display list, or <see langword="null"/> when no depth feature is adjusted.</returns>
    public static async Task<Result?> ApplyAsync(
        IReadOnlyList<DrawingInstruction> charted,
        IReadOnlyDictionary<long, Feature> features,
        IDepthAdjustment adjustment,
        Func<double, double, int?> datumAt,
        Func<S101DepthShift, CancellationToken, Task<LuaPortrayalSession>> openSession,
        CancellationToken cancellationToken)
    {
        var factory = new GeometryFactory();
        if (Footprint(factory, adjustment.Footprint) is not { IsEmpty: false } footprint)
        {
            return null;
        }

        var heights = new Dictionary<uint, double>();
        var soundings = new HashSet<long>();
        var split = new Dictionary<long, (NtsGeometry Inside, NtsGeometry Outside)>();
        foreach (var feature in features.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DepthFeatureTypes.Contains(feature.FeatureType) || feature.Coordinates.Count == 0)
            {
                continue;
            }

            if (feature.FeatureType == "Sounding")
            {
                if (feature.Coordinates.Any(p => footprint.Covers(Point(factory, p)) && At(adjustment, datumAt, p) is not null))
                {
                    soundings.Add(feature.Id);
                }

                continue;
            }

            if (ToGeometry(factory, feature) is not { IsEmpty: false } geometry || !geometry.Intersects(footprint))
            {
                continue;
            }

            var inside = footprint.Covers(geometry) ? geometry : geometry.Intersection(footprint);
            if (LowestHeight(factory, inside, adjustment, datumAt) is not { } h)
            {
                continue;
            }

            heights[(uint)feature.Id] = Math.Floor(h / Bucket) * Bucket;
            if (!ReferenceEquals(inside, geometry))
            {
                split[feature.Id] = (inside, geometry.Difference(footprint));
            }
        }

        if (heights.Count == 0 && soundings.Count == 0)
        {
            return null;
        }

        var shift = new S101DepthShift(
            heights,
            (lat, lon) => footprint.Covers(factory.CreatePoint(new Coordinate(lon, lat)))
                ? At(adjustment, datumAt, new GeoPosition(lat, lon))
                : null);
        IReadOnlyList<DrawingInstruction> adjusted;
        using (var session = await openSession(shift, cancellationToken).ConfigureAwait(false))
        {
            var ids = heights.Keys.Select(k => k.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Concat(soundings.Select(s => s.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            adjusted = session.Portray(ids);
        }

        var replaced = new HashSet<long>(heights.Keys.Select(k => (long)k).Concat(soundings));
        var result = new List<DrawingInstruction>(charted.Count + adjusted.Count);
        var geometryById = new Dictionary<long, FeatureGeometry>();
        var sourceIds = new Dictionary<long, long>();
        foreach (var instruction in charted)
        {
            if (!TryId(instruction, out var id) || !replaced.Contains(id))
            {
                result.Add(instruction);
            }
            else if (split.TryGetValue(id, out var parts) && instruction is LineInstruction or AreaInstruction)
            {
                // The part outside the footprint stays charted.
                result.AddRange(Retarget(factory, instruction, id + OutsideIdOffset, parts.Outside));
            }
        }

        foreach (var instruction in adjusted)
        {
            if (!TryId(instruction, out var id))
            {
                continue;
            }

            if (split.TryGetValue(id, out var parts))
            {
                result.AddRange(Retarget(factory, instruction, id + InsideIdOffset, parts.Inside));
            }
            else
            {
                result.Add(instruction);
            }
        }

        foreach (var (id, parts) in split)
        {
            if (ToFeatureGeometry(parts.Inside) is { } inside)
            {
                geometryById[id + InsideIdOffset] = inside;
                sourceIds[id + InsideIdOffset] = id;
            }

            if (ToFeatureGeometry(parts.Outside) is { } outside)
            {
                geometryById[id + OutsideIdOffset] = outside;
                sourceIds[id + OutsideIdOffset] = id;
            }
        }

        result.AddRange(Markers(adjusted, soundings, shift));
        return new Result(
            result,
            geometryById,
            sourceIds,
            heights.ToDictionary(kv => (long)kv.Key, kv => kv.Value),
            soundings);
    }

    /// <summary>The water level at a point under the adjustment's datum rule.</summary>
    private static double? At(IDepthAdjustment adjustment, Func<double, double, int?> datumAt, GeoPosition p) =>
        adjustment.At(p.Latitude, p.Longitude, datumAt(p.Latitude, p.Longitude))?.HeightMeters;

    // The lowest water level over a geometry: its vertices and a lattice over
    // its envelope, inside it. Null when none of them has a water level.
    private static double? LowestHeight(GeometryFactory factory, NtsGeometry geometry, IDepthAdjustment adjustment, Func<double, double, int?> datumAt)
    {
        double? lowest = null;
        void Consider(Coordinate c)
        {
            if (At(adjustment, datumAt, new GeoPosition(c.Y, c.X)) is { } h && (lowest is not { } l || h < l))
            {
                lowest = h;
            }
        }

        foreach (var c in geometry.Coordinates)
        {
            Consider(c);
        }

        if (geometry.Dimension == Dimension.Surface)
        {
            var env = geometry.EnvelopeInternal;
            for (var i = 0; i <= Lattice; i++)
            {
                for (var j = 0; j <= Lattice; j++)
                {
                    var c = new Coordinate(env.MinX + (env.Width * i / Lattice), env.MinY + (env.Height * j / Lattice));
                    if (geometry.Covers(factory.CreatePoint(c)))
                    {
                        Consider(c);
                    }
                }
            }
        }

        return lowest;
    }

    // Moves an instruction onto a synthetic feature. A line drawn along its
    // own coordinates is clipped to the part; anything else draws with the
    // part's geometry.
    private static IEnumerable<DrawingInstruction> Retarget(GeometryFactory factory, DrawingInstruction instruction, long id, NtsGeometry part)
    {
        var reference = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (instruction)
        {
            case LineInstruction { CoordinatesOverride: { Count: >= 2 } coordinates } line:
                var clipped = factory.CreateLineString([.. coordinates.Select(p => new Coordinate(p.Longitude, p.Latitude))])
                    .Intersection(part.Dimension == Dimension.Surface ? part : part.Buffer(1e-9));
                for (var i = 0; i < clipped.NumGeometries; i++)
                {
                    if (clipped.GetGeometryN(i) is LineString { NumPoints: >= 2 } piece)
                    {
                        yield return Copy(line, reference, [.. piece.Coordinates.Select(c => new GeoPosition(c.Y, c.X))]);
                    }
                }

                break;
            case LineInstruction line:
                yield return Copy(line, reference, null);
                break;
            case AreaInstruction area:
                yield return Copy(area, reference);
                break;
            case PointInstruction point:
                yield return Copy(point, reference);
                break;
            case TextInstruction text:
                yield return Copy(text, reference);
                break;
        }
    }

    // One marker per adjusted sounding position, after its figure.
    private static IEnumerable<DrawingInstruction> Markers(IReadOnlyList<DrawingInstruction> adjusted, IReadOnlySet<long> soundings, S101DepthShift shift)
    {
        var seen = new HashSet<GeoPosition>();
        foreach (var instruction in adjusted)
        {
            if (instruction is not PointInstruction { CoordinateOverride: { } at } point
                || !TryId(point, out var id)
                || !soundings.Contains(id)
                || shift.ForSounding(at.Latitude, at.Longitude) is null
                || !seen.Add(at))
            {
                continue;
            }

            yield return new TextInstruction
            {
                FeatureReference = point.FeatureReference,
                Plane = point.Plane,
                ViewingGroup = point.ViewingGroup,
                AdditionalViewingGroups = point.AdditionalViewingGroups,
                DrawingPriority = point.DrawingPriority,
                ScaleMinimum = point.ScaleMinimum,
                ScaleMaximum = point.ScaleMaximum,
                TimeValid = point.TimeValid,
                Text = SoundingMarker,
                FontSize = 6,
                FontColor = "SNDG2",
                CoordinateOverride = at,
                OffsetX = 3.2,
                HorizontalAlignment = TextHorizontalAlignment.Start,
            };
        }
    }

    private static bool TryId(DrawingInstruction instruction, out long id) =>
        long.TryParse(instruction.FeatureReference, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id);

    private static NtsGeometry? Footprint(GeometryFactory factory, IReadOnlyList<CoverageArea> areas)
    {
        var polygons = new List<NtsGeometry>();
        foreach (var area in areas)
        {
            if (Ring(factory, area.ExteriorRing) is { } shell)
            {
                var polygon = factory.CreatePolygon(shell, [.. area.InteriorRings.Select(r => Ring(factory, r)).OfType<LinearRing>()]);
                polygons.Add(polygon.IsValid ? polygon : polygon.Buffer(0));
            }
        }

        return polygons.Count == 0 ? null : CascadedPolygonUnion.Union(polygons);
    }

    private static NtsGeometry? ToGeometry(GeometryFactory factory, Feature feature)
    {
        try
        {
            switch (feature.GeometryType)
            {
                case GeometryType.Point:
                    return Point(factory, feature.Coordinates[0]);
                case GeometryType.Curve:
                    var curves = feature.Curves.Count > 0 ? feature.Curves : [feature.Coordinates];
                    var lines = curves.Where(c => c.Count >= 2)
                        .Select(c => (NtsGeometry)factory.CreateLineString([.. c.Select(p => new Coordinate(p.Longitude, p.Latitude))]))
                        .ToArray();
                    return lines.Length == 0 ? null : factory.BuildGeometry(lines);
                case GeometryType.Surface:
                    if (Ring(factory, feature.Coordinates) is not { } shell)
                        return null;
                    var polygon = factory.CreatePolygon(shell, [.. feature.InteriorRings.Select(r => Ring(factory, r)).OfType<LinearRing>()]);
                    return polygon.IsValid ? polygon : polygon.Buffer(0);
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or TopologyException)
        {
            // Geometry NTS can't build stays charted.
            return null;
        }
    }

    private static Point Point(GeometryFactory factory, GeoPosition p) => factory.CreatePoint(new Coordinate(p.Longitude, p.Latitude));

    private static LinearRing? Ring(GeometryFactory factory, IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = ring.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToList();
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0]);

        return coordinates.Count >= 4 ? factory.CreateLinearRing([.. coordinates]) : null;
    }

    private static FeatureGeometry? ToFeatureGeometry(NtsGeometry geometry)
    {
        var polygons = new List<Polygon>();
        var lines = new List<LineString>();
        for (var i = 0; i < geometry.NumGeometries; i++)
        {
            switch (geometry.GetGeometryN(i))
            {
                case Polygon { IsEmpty: false } p:
                    polygons.Add(p);
                    break;
                case LineString { IsEmpty: false } l:
                    lines.Add(l);
                    break;
            }
        }

        static IReadOnlyList<GeoPosition> Positions(Coordinate[] coordinates) => [.. coordinates.Select(c => new GeoPosition(c.Y, c.X))];

        if (polygons.Count > 0)
        {
            return new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = Positions(polygons[0].ExteriorRing.Coordinates),
                InteriorRings = [.. polygons[0].InteriorRings.Select(r => Positions(r.Coordinates))],
                Parts = [.. polygons.Select(p => Positions(p.ExteriorRing.Coordinates))],
                PartInteriorRings = [.. polygons.Select(p => (IReadOnlyList<IReadOnlyList<GeoPosition>>)[.. p.InteriorRings.Select(r => Positions(r.Coordinates))])],
            };
        }

        if (lines.Count > 0)
        {
            return new FeatureGeometry
            {
                Type = GeometryType.Curve,
                Coordinates = Positions(lines[0].Coordinates),
                Parts = [.. lines.Select(l => Positions(l.Coordinates))],
            };
        }

        return null;
    }

    private static LineInstruction Copy(LineInstruction l, string reference, IReadOnlyList<GeoPosition>? coordinates) => new()
    {
        FeatureReference = reference,
        Plane = l.Plane, ViewingGroup = l.ViewingGroup, AdditionalViewingGroups = l.AdditionalViewingGroups,
        DrawingPriority = l.DrawingPriority, ScaleMinimum = l.ScaleMinimum, ScaleMaximum = l.ScaleMaximum,
        TimeValid = l.TimeValid, AlertReference = l.AlertReference,
        LineStyleReference = l.LineStyleReference, LineWidth = l.LineWidth, LineColor = l.LineColor,
        Dashes = l.Dashes, DashIntervalLength = l.DashIntervalLength,
        CoordinatesOverride = coordinates ?? l.CoordinatesOverride, Suppression = l.Suppression,
    };

    private static AreaInstruction Copy(AreaInstruction a, string reference) => new()
    {
        FeatureReference = reference,
        Plane = a.Plane, ViewingGroup = a.ViewingGroup, AdditionalViewingGroups = a.AdditionalViewingGroups,
        DrawingPriority = a.DrawingPriority, ScaleMinimum = a.ScaleMinimum, ScaleMaximum = a.ScaleMaximum,
        TimeValid = a.TimeValid, AlertReference = a.AlertReference,
        AreaFillReference = a.AreaFillReference, FillColor = a.FillColor, Transparency = a.Transparency,
        OutlineStyleReference = a.OutlineStyleReference,
    };

    private static PointInstruction Copy(PointInstruction p, string reference) => new()
    {
        FeatureReference = reference,
        Plane = p.Plane, ViewingGroup = p.ViewingGroup, AdditionalViewingGroups = p.AdditionalViewingGroups,
        DrawingPriority = p.DrawingPriority, ScaleMinimum = p.ScaleMinimum, ScaleMaximum = p.ScaleMaximum,
        TimeValid = p.TimeValid, AlertReference = p.AlertReference,
        SymbolReference = p.SymbolReference, SymbolScale = p.SymbolScale, Rotation = p.Rotation, RotationCrs = p.RotationCrs,
        LocalOffsetX = p.LocalOffsetX, LocalOffsetY = p.LocalOffsetY, LinePlacementPosition = p.LinePlacementPosition,
        CoordinateOverride = p.CoordinateOverride, AreaPlacement = p.AreaPlacement,
    };

    private static TextInstruction Copy(TextInstruction t, string reference) => new()
    {
        FeatureReference = reference,
        Plane = t.Plane, ViewingGroup = t.ViewingGroup, AdditionalViewingGroups = t.AdditionalViewingGroups,
        DrawingPriority = t.DrawingPriority, ScaleMinimum = t.ScaleMinimum, ScaleMaximum = t.ScaleMaximum,
        TimeValid = t.TimeValid, AlertReference = t.AlertReference,
        Text = t.Text, FontReference = t.FontReference, FontSize = t.FontSize, FontColor = t.FontColor,
        FontTransparency = t.FontTransparency, BackgroundColor = t.BackgroundColor, BackgroundTransparency = t.BackgroundTransparency,
        Rotation = t.Rotation, LinePlacementPosition = t.LinePlacementPosition,
        HorizontalAlignment = t.HorizontalAlignment, VerticalAlignment = t.VerticalAlignment,
        OffsetX = t.OffsetX, OffsetY = t.OffsetY, LineStartOffset = t.LineStartOffset, LineEndOffset = t.LineEndOffset,
        LineOffsetMode = t.LineOffsetMode, CoordinateOverride = t.CoordinateOverride, AreaPlacement = t.AreaPlacement,
        FontWeight = t.FontWeight, FontProportion = t.FontProportion, HaloColor = t.HaloColor, HaloWidth = t.HaloWidth,
    };
}
