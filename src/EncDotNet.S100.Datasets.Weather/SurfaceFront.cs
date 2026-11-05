using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// One front or trough of a surface analysis: its kind, the intensity the
/// bulletin gives it, and its points in the bulletin's order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which side the pips go on.</b> The points run in the order the bulletin
/// lists them, and the front's symbols (the triangles of a cold front, the
/// semicircles of a warm front, both for an occluded front) belong on the
/// <b>left</b> of the line walking from the first point to the last. That's
/// the side the front moves towards. WPC's format pages don't state the rule;
/// it's established from the bulletins themselves (see
/// <see cref="CodedSurfaceBulletinReader"/> for the evidence). A stationary
/// front alternates its triangles and semicircles on opposite sides; the
/// bulletin doesn't say which goes where, so drawing it is a convention, not
/// data. A trough has no symbols.
/// </para>
/// <para>
/// <b>Longitudes.</b> Points are in WGS-84 degrees, longitude positive east in
/// −180 to 180. The bulletin codes longitude as degrees west, so a value over
/// 180°W is east of the antimeridian (181°W is 179°E). A front that crosses
/// 180° has neighbouring points whose longitudes differ by more than 180°
/// (<see cref="CrossesAntimeridian"/>); a host joins them the short way round.
/// </para>
/// <para>
/// Points are kept exactly as coded, repeats included (WPC sometimes repeats
/// a front's last point); smoothing and de-duplication are for drawing.
/// </para>
/// </remarks>
public sealed class SurfaceFront
{
    /// <summary>Creates a front.</summary>
    /// <param name="type">The kind of front.</param>
    /// <param name="strength">The intensity the bulletin gives, or <see cref="SurfaceFrontStrength.Unspecified"/>.</param>
    /// <param name="points">The points in the bulletin's order. The list is copied.</param>
    public SurfaceFront(SurfaceFrontType type, SurfaceFrontStrength strength, IEnumerable<GeoPosition> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Type = type;
        Strength = strength;
        Points = points.ToArray();
    }

    /// <summary>The kind of front.</summary>
    public SurfaceFrontType Type { get; }

    /// <summary>The intensity the bulletin gives the front.</summary>
    public SurfaceFrontStrength Strength { get; }

    /// <summary>
    /// The points in the bulletin's order: the front's symbols go on the left
    /// walking from the first to the last.
    /// </summary>
    public IReadOnlyList<GeoPosition> Points { get; }

    /// <summary>The bulletin's code for the front's kind: <c>COLD</c>, <c>WARM</c>, <c>OCFNT</c>, <c>STNRY</c> or <c>TROF</c>.</summary>
    public string Code => CodeOf(Type);

    /// <summary>
    /// Whether two neighbouring points are more than 180° of longitude apart,
    /// so the line between them crosses the antimeridian.
    /// </summary>
    public bool CrossesAntimeridian
    {
        get
        {
            for (int i = 1; i < Points.Count; i++)
            {
                if (Math.Abs(Points[i].Longitude - Points[i - 1].Longitude) > 180)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The bulletin's code for a kind of front.</summary>
    /// <param name="type">The kind of front.</param>
    public static string CodeOf(SurfaceFrontType type) => type switch
    {
        SurfaceFrontType.Cold => "COLD",
        SurfaceFrontType.Warm => "WARM",
        SurfaceFrontType.Occluded => "OCFNT",
        SurfaceFrontType.Stationary => "STNRY",
        SurfaceFrontType.Trough => "TROF",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
