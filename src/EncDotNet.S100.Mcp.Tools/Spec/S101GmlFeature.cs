using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;
using PipelineGeometryType = EncDotNet.S100.Pipelines.Vector.GeometryType;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Adapts a resolved S-101 <see cref="Feature"/> (as produced by
/// <see cref="EncDotNet.S100.Datasets.S101.S101VectorSource"/>) to the
/// product-agnostic <see cref="IGmlFeature"/> shape so that the generic
/// <see cref="QueryFeaturesTool"/> can enumerate, filter, and bbox-intersect
/// ISO 8211-encoded S-101 features alongside the GML-encoded products.
/// </summary>
/// <remarks>
/// <para>
/// S-101 is not GML-encoded (S-100 Part 10a, ISO 8211); this adapter exists
/// purely so the shared <see cref="GmlFeatureGeometry"/> helpers — which
/// operate over <see cref="IGmlFeature.Points"/> /
/// <see cref="IGmlFeature.Curves"/> / <see cref="IGmlFeature.ExteriorRing"/> —
/// can compute a bounding box and intersection for an S-101 feature without
/// duplicating the query/paging logic.
/// </para>
/// <para>
/// <see cref="Id"/> is the feature record's decimal RCID — the same
/// identifier accepted by <see cref="S101FeatureDescriber"/>. The geometry
/// primitive determines which bucket the resolved coordinates land in:
/// <see cref="GeometryType.Point"/> → <see cref="Points"/>,
/// <see cref="GeometryType.Curve"/> → <see cref="Curves"/>,
/// <see cref="GeometryType.Surface"/> → <see cref="ExteriorRing"/> /
/// <see cref="InteriorRings"/>.
/// </para>
/// </remarks>
internal sealed class S101GmlFeature : IGmlFeature
{
    private S101GmlFeature(
        string id,
        string featureType,
        GmlGeometryType geometryType,
        ImmutableArray<(double Latitude, double Longitude)> points,
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> curves,
        ImmutableArray<(double Latitude, double Longitude)> exteriorRing,
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> interiorRings,
        ImmutableDictionary<string, string> attributes)
    {
        Id = id;
        FeatureType = featureType;
        GeometryType = geometryType;
        Points = points;
        Curves = curves;
        ExteriorRing = exteriorRing;
        InteriorRings = interiorRings;
        Attributes = attributes;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string FeatureType { get; }

    /// <inheritdoc />
    public GmlGeometryType GeometryType { get; }

    /// <inheritdoc />
    public ImmutableArray<(double Latitude, double Longitude)> Points { get; }

    /// <inheritdoc />
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> Curves { get; }

    /// <inheritdoc />
    public ImmutableArray<(double Latitude, double Longitude)> ExteriorRing { get; }

    /// <inheritdoc />
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> InteriorRings { get; }

    /// <inheritdoc />
    public ImmutableDictionary<string, string> Attributes { get; }

    /// <inheritdoc />
    public IEnumerable<IGmlComplexAttribute> GmlComplexAttributes => [];

    /// <summary>
    /// Projects a resolved S-101 pipeline <paramref name="feature"/> onto the
    /// <see cref="IGmlFeature"/> shape.
    /// </summary>
    public static S101GmlFeature FromFeature(Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var coords = ToImmutable(feature.Coordinates);

        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exterior = ImmutableArray<(double, double)>.Empty;
        var interior = ImmutableArray<ImmutableArray<(double, double)>>.Empty;

        switch (feature.GeometryType)
        {
            case PipelineGeometryType.Point:
                points = coords;
                break;
            case PipelineGeometryType.Curve:
                curves = coords.IsDefaultOrEmpty
                    ? ImmutableArray<ImmutableArray<(double, double)>>.Empty
                    : ImmutableArray.Create(coords);
                break;
            case PipelineGeometryType.Surface:
                exterior = coords;
                interior = ToImmutableRings(feature.InteriorRings);
                break;
        }

        var attributes = ImmutableDictionary<string, string>.Empty;
        if (feature.Attributes.Count > 0)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in feature.Attributes)
            {
                builder[key] = value switch
                {
                    null => string.Empty,
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty,
                };
            }
            attributes = builder.ToImmutable();
        }

        return new S101GmlFeature(
            feature.Id.ToString(CultureInfo.InvariantCulture),
            feature.FeatureType,
            MapGeometryType(feature.GeometryType),
            points,
            curves,
            exterior,
            interior,
            attributes);
    }

    private static GmlGeometryType MapGeometryType(PipelineGeometryType type) => type switch
    {
        PipelineGeometryType.Point => GmlGeometryType.Point,
        PipelineGeometryType.Curve => GmlGeometryType.Curve,
        PipelineGeometryType.Surface => GmlGeometryType.Surface,
        _ => GmlGeometryType.None,
    };

    private static ImmutableArray<(double Latitude, double Longitude)> ToImmutable(
        IReadOnlyList<(double Latitude, double Longitude)> coords)
    {
        if (coords.Count == 0) return ImmutableArray<(double, double)>.Empty;
        var builder = ImmutableArray.CreateBuilder<(double, double)>(coords.Count);
        foreach (var c in coords) builder.Add(c);
        return builder.MoveToImmutable();
    }

    private static ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> ToImmutableRings(
        IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> rings)
    {
        if (rings.Count == 0) return ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>(rings.Count);
        foreach (var ring in rings) builder.Add(ToImmutable(ring));
        return builder.MoveToImmutable();
    }
}
