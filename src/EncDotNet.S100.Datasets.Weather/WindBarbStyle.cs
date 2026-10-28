using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// WMO wind barbs from the 10 m wind speed (<c>wind.speed</c>) and the
/// direction it blows from (<c>wind.from_direction</c>): one symbol per 5 kn
/// band from 5 to 200 kn, and a calm circle below 2.5 kn. On by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Direction.</b> Each barb symbol is drawn with its staff running up
/// (north) from the station, its pivot, to the feathers. The arrow renderers
/// rotate a symbol clockwise by <see cref="CoverageSymbolScheme.RotationFor"/>,
/// so with no rotation offset a barb rotated by the "from" direction has its
/// staff pointing into the wind: a westerly (270°) points west from the
/// station. (The S-111 arrow, whose "up" is its head, is rotated by the
/// direction the current flows toward, the opposite convention.)
/// </para>
/// <para>
/// <b>Feathers.</b> Pennants are 50 kn, full barbs 10 kn and half barbs 5 kn
/// (<see cref="WindBarb"/>), on the clockwise side of the staff in the
/// northern hemisphere (<see cref="Bands"/>) and on the anticlockwise side,
/// the mirror image, in the southern hemisphere (<see cref="SouthernBands"/>).
/// The scheme picks the set by each cell's latitude
/// (<see cref="CoverageSymbolScheme.SouthernHemisphereBands"/>), the same rule
/// as <see cref="WindBarbSymbols.For(double, double)"/>.
/// </para>
/// <para>
/// <b>Units.</b> The bands are defined in knots and converted to metres per
/// second, the stored unit of <c>wind.speed</c> (see
/// <see cref="WindSpeedStyle"/> for why). The 200 kn band is open-ended.
/// </para>
/// </remarks>
public sealed class WindBarbStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.wind.barbs"</c>.</summary>
    public const string Key = "weather.wind.barbs";

    /// <summary>
    /// The length a barb is thinned by, in millimetres: about the staff and
    /// its feathers (the symbols' view box is twice that, centred on the station).
    /// </summary>
    public const double NominalSymbolLengthMillimetres = 9.0;

    /// <summary>The northern-hemisphere symbol bands, calm first, in metres per second.</summary>
    public static IReadOnlyList<SymbolBand> Bands { get; } = BuildBands(southern: false);

    /// <summary>
    /// The southern-hemisphere symbol bands: the same ranges as
    /// <see cref="Bands"/>, with the mirrored <c>WXBARBS</c> symbols.
    /// </summary>
    public static IReadOnlyList<SymbolBand> SouthernBands { get; } = BuildBands(southern: true);

    private static readonly CoverageSymbolScheme Scheme = new()
    {
        ValueFieldName = WeatherParameters.WindSpeed,
        RotationFieldName = WeatherParameters.WindFromDirection,
        Bands = Bands,
        SouthernHemisphereBands = SouthernBands,
        RotationOffsetDegrees = 0,
        NominalSymbolLengthMillimetres = NominalSymbolLengthMillimetres,
    };

    /// <summary>Creates the style, with the Day palette active.</summary>
    public WindBarbStyle()
        : base(Key, "Wind barbs", WeatherLayerKind.Symbols, WeatherParameters.WindSpeed,
            WeatherParameters.WindFromDirection, isVisibleByDefault: true)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The scheme is the same for every palette: the symbols' colours come from the sub-layer's palette.</remarks>
    public override CoverageSymbolScheme ResolveSymbolScheme(MarinerSettings settings) => Scheme;

    private static SymbolBand[] BuildBands(bool southern)
    {
        var barbs = WindBarb.All;
        var bands = new SymbolBand[barbs.Count];
        for (int i = 0; i < barbs.Count; i++)
        {
            var barb = barbs[i];
            double lowerKnots = barb.IsCalm ? 0 : barb.Knots - WindBarb.CalmBelowKnots;
            double upperKnots = barb.Knots + WindBarb.CalmBelowKnots;
            bands[i] = new SymbolBand
            {
                MinValue = (float)Speed.FromKnots(lowerKnots).TotalMetresPerSecond,
                MaxValue = barb.Knots == WindBarb.MaxKnots
                    ? float.PositiveInfinity
                    : (float)Speed.FromKnots(upperKnots).TotalMetresPerSecond,
                SymbolRef = southern ? barb.SouthernSymbolName : barb.SymbolName,
                ScaleFactor = 1.0f,
                Label = barb.IsCalm ? "Calm" : $"{barb.Knots} kn",
            };
        }

        return bands;
    }
}
