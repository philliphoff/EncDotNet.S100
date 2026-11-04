using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Surface current arrows (issue #1228): one arrow per grid cell, pointing
/// the way the water flows, coloured and sized by the speed in S-111's nine
/// speed bands. On by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bands.</b> The bands, colours and sizes follow the S-111 Ed 2.0.0
/// portrayal catalogue (<c>select_arrow.xsl</c>), so a model current and an
/// S-111 current of the same speed look the same: below 0.5, 0.5–1, 1–2, 2–3,
/// 3–5, 5–7, 7–10, 10–13 and 13 kn and faster. The three slowest bands draw
/// at scale 0.4, the next five grow with the speed (0.2 per knot, so 2 kn is
/// 0.4 and 13 kn is 2.6), and the fastest band stays at 2.6. The field is
/// stored in metres per second, so the bounds and the per-knot factor are
/// converted.
/// </para>
/// <para>
/// <b>Direction.</b> <c>current.to_direction</c> is the direction the water
/// flows to, and the arrow points up (north) at rotation 0, so no offset is
/// needed.
/// </para>
/// <para>
/// <b>S-111.</b> The arrows set
/// <see cref="EncDotNet.S100.Datasets.Pipelines.Portrayal.ArrowCoverageSubLayer.YieldsToS111"/>:
/// where an S-111 dataset is loaded, the S-98 rule <c>R-111-WX-B</c> hides
/// them inside its coverage, so the official forecast is the one drawn.
/// </para>
/// </remarks>
public sealed class CurrentArrowStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.current.arrows"</c>.</summary>
    public const string Key = "weather.current.arrows";

    /// <summary>The prefix of the arrow symbols, followed by the band number: <c>WXCURR1</c> to <c>WXCURR9</c>.</summary>
    public const string SymbolPrefix = "WXCURR";

    /// <summary>The arrow's length at scale 1 for thinning, in millimetres: 10, as S-111's.</summary>
    public const double NominalSymbolLengthMillimetres = 10.0;

    /// <summary>The scale of the three slowest bands, as S-111's <c>scaleFloor</c>.</summary>
    public const float ScaleFloor = 0.4f;

    /// <summary>The scale of the fastest band, as S-111's <c>scaleCeiling</c>.</summary>
    public const float ScaleCeiling = 2.6f;

    /// <summary>The scale per knot of the middle bands, as S-111's <c>scaleFactorIntermediate</c>.</summary>
    public const float ScalePerKnot = 0.2f;

    // The band bounds, in knots, as S-111's.
    private static readonly float[] BoundsKnots = [0f, 0.5f, 1f, 2f, 3f, 5f, 7f, 10f, 13f];

    /// <summary>The symbol bands, in metres per second.</summary>
    public static IReadOnlyList<SymbolBand> Bands { get; } = BuildBands();

    private static readonly CoverageSymbolScheme Scheme = new()
    {
        ValueFieldName = WeatherParameters.CurrentSpeed,
        RotationFieldName = WeatherParameters.CurrentToDirection,
        Bands = Bands,
        NominalSymbolLengthMillimetres = NominalSymbolLengthMillimetres,
    };

    /// <summary>Creates the style, with the Day palette active.</summary>
    public CurrentArrowStyle()
        : base(Key, "Currents", WeatherLayerKind.Symbols, WeatherParameters.CurrentSpeed,
            WeatherParameters.CurrentToDirection, isVisibleByDefault: true)
    {
    }

    /// <summary>The arrow symbol of band <paramref name="band"/>, 1 (slowest) to 9.</summary>
    /// <param name="band">The band number.</param>
    public static string SymbolName(int band)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(band, BoundsKnots.Length);
        return SymbolPrefix + band.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    /// <remarks>The scheme is the same for every palette: the band colours are palette classes in the symbols.</remarks>
    public override CoverageSymbolScheme ResolveSymbolScheme(MarinerSettings settings) => Scheme;

    private static SymbolBand[] BuildBands()
    {
        float metresPerSecondPerKnot = (float)Speed.FromKnots(1).TotalMetresPerSecond;
        var bands = new SymbolBand[BoundsKnots.Length];
        for (int i = 0; i < BoundsKnots.Length; i++)
        {
            float lower = BoundsKnots[i];
            bool last = i == BoundsKnots.Length - 1;
            float upper = last ? float.PositiveInfinity : BoundsKnots[i + 1];
            bool scaled = i is >= 3 and <= 7;
            bands[i] = new SymbolBand
            {
                MinValue = lower * metresPerSecondPerKnot,
                MaxValue = last ? float.PositiveInfinity : upper * metresPerSecondPerKnot,
                SymbolRef = SymbolName(i + 1),
                ScaleByValue = scaled,
                ScaleFactor = scaled ? ScalePerKnot / metresPerSecondPerKnot : last ? ScaleCeiling : ScaleFloor,
                Label = Label(lower, upper, first: i == 0, last),
            };
        }

        return bands;
    }

    private static string Label(float lower, float upper, bool first, bool last) =>
        first ? string.Create(CultureInfo.InvariantCulture, $"Under {upper:0.#} kn")
        : last ? string.Create(CultureInfo.InvariantCulture, $"{lower:0.#} kn and faster")
        : string.Create(CultureInfo.InvariantCulture, $"{lower:0.#}–{upper:0.#} kn");
}
