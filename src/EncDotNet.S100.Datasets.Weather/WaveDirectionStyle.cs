using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Wave direction arrows: one arrow symbol (<see cref="ArrowSymbol"/>)
/// pointing the way the waves travel, sized by the significant wave height
/// (<c>wave.hs</c>). Off by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Direction.</b> <c>wave.dir</c> is the direction the waves come
/// <em>from</em>, and the arrow symbol points up (north) at rotation 0, so
/// the scheme adds <see cref="CoverageSymbolScheme.RotationOffsetDegrees"/>
/// of 180: waves from the west (270°) draw an arrow pointing east.
/// </para>
/// <para>
/// <b>Size.</b> The arrow is drawn at half size below 1.5 m, grows with the
/// height from 1.5 m (scale 0.5) to 6 m (scale 2), and stays at scale 2
/// above that. Below 0.1 m no arrow is drawn: the direction of a flat sea
/// says little.
/// </para>
/// </remarks>
public sealed class WaveDirectionStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.wave.direction"</c>.</summary>
    public const string Key = "weather.wave.direction";

    /// <summary>The wave arrow symbol.</summary>
    public const string ArrowSymbol = "WXWAVE01";

    /// <summary>The arrow's length at scale 1, in millimetres.</summary>
    public const double NominalSymbolLengthMillimetres = 9.0;

    /// <summary>The height below which no arrow is drawn, in metres.</summary>
    public const float MinimumHeight = 0.1f;

    /// <summary>The symbol bands, in metres.</summary>
    public static IReadOnlyList<SymbolBand> Bands { get; } =
    [
        new() { MinValue = MinimumHeight, MaxValue = 1.5f, SymbolRef = ArrowSymbol, ScaleFactor = 0.5f, Label = "Under 1.5 m" },
        new() { MinValue = 1.5f, MaxValue = 6f, SymbolRef = ArrowSymbol, ScaleByValue = true, ScaleFactor = 1f / 3f, Label = "1.5–6 m" },
        new() { MinValue = 6f, MaxValue = float.PositiveInfinity, SymbolRef = ArrowSymbol, ScaleFactor = 2f, Label = "6 m and higher" },
    ];

    private static readonly CoverageSymbolScheme Scheme = new()
    {
        ValueFieldName = WeatherParameters.WaveHeight,
        RotationFieldName = WeatherParameters.WaveDirection,
        Bands = Bands,
        RotationOffsetDegrees = 180,
        NominalSymbolLengthMillimetres = NominalSymbolLengthMillimetres,
    };

    /// <summary>Creates the style, with the Day palette active.</summary>
    public WaveDirectionStyle()
        : base(Key, "Wave direction", WeatherLayerKind.Symbols, WeatherParameters.WaveHeight,
            WeatherParameters.WaveDirection, isVisibleByDefault: false)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The scheme is the same for every palette: the symbol's colour comes from the sub-layer's palette.</remarks>
    public override CoverageSymbolScheme ResolveSymbolScheme(MarinerSettings settings) => Scheme;
}
