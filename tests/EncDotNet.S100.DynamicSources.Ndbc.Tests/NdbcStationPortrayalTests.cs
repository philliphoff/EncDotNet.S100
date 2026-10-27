using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

public sealed class NdbcStationPortrayalTests
{
    private static readonly DateTimeOffset Observed = new(2026, 10, 27, 14, 50, 0, TimeSpan.Zero);
    private static readonly NdbcStationPortrayal Portrayal = NdbcStationPortrayal.Instance;

    internal static DynamicFeature Station(NdbcObservation observation, bool stale = false, double latitude = 42.346, double longitude = -70.651) => new()
    {
        Id = NdbcObservationSource.FeatureIdFor(observation.StationId),
        Kind = stale ? NdbcObservationSource.StaleFeatureKind : NdbcObservationSource.FeatureKind,
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(latitude, longitude)],
        Attributes = new Dictionary<string, object?>
        {
            [NdbcObservationSource.ObservationAttribute] = observation,
            [NdbcObservationSource.StaleAttribute] = stale,
        },
        LastUpdated = observation.Time,
    };

    internal static NdbcObservation Windy() => new()
    {
        StationId = "44013",
        Time = Observed,
        WindFromDirection = Angle.FromDegrees(230),
        WindSpeed = Speed.FromMetresPerSecond(7.4), // 14.4 kn: the 15 kn barb
        WaveHeight = Length.FromMetres(1.14),
    };

    [Fact]
    public void A_station_is_a_marker_a_barb_into_the_wind_and_a_wave_height_label()
    {
        var instructions = Portrayal.Portray(Station(Windy()));

        Assert.Equal(3, instructions.Count);
        var marker = Assert.IsType<PointInstruction>(instructions[0]);
        Assert.Equal(NdbcStationPortrayal.StationSymbol, marker.SymbolReference);

        var barb = Assert.IsType<PointInstruction>(instructions[1]);
        Assert.Equal("WXBARBN015", barb.SymbolReference);
        Assert.Equal(230, barb.Rotation);
        Assert.Equal(SymbolRotationCrs.Geographic, barb.RotationCrs);
        Assert.True(barb.DrawingPriority > marker.DrawingPriority);

        var label = Assert.IsType<TextInstruction>(instructions[2]);
        Assert.Equal("1.1 m", label.Text);
        Assert.Equal(NdbcStationPortrayal.LabelMaxScaleDenominator, label.ScaleMinimum);
        Assert.All(instructions, i => Assert.Equal("ndbc:44013", i.FeatureReference));
    }

    [Fact]
    public void A_calm_wind_is_an_unrotated_circle()
    {
        var instructions = Portrayal.Portray(Station(Windy() with { WindSpeed = Speed.FromKnots(1) }));

        var calm = Assert.IsType<PointInstruction>(instructions[1]);
        Assert.Equal("WXCALM", calm.SymbolReference);
        Assert.Null(calm.Rotation);
    }

    [Fact]
    public void A_station_without_wind_direction_or_waves_is_just_a_marker()
    {
        var observation = Windy() with { WindFromDirection = null, WaveHeight = null };

        var instruction = Assert.Single(Portrayal.Portray(Station(observation)));

        Assert.Equal(NdbcStationPortrayal.StationSymbol, Assert.IsType<PointInstruction>(instruction).SymbolReference);
    }

    [Fact]
    public void A_stale_station_uses_the_grey_symbols()
    {
        var instructions = Portrayal.Portray(Station(Windy(), stale: true));

        var symbols = instructions.OfType<PointInstruction>().Select(p => p.SymbolReference!).ToArray();
        Assert.Equal([NdbcStationPortrayal.StaleSymbolPrefix + "NDBCSTN", NdbcStationPortrayal.StaleSymbolPrefix + "WXBARBN015"], symbols);
        Assert.Equal(NdbcStationPortrayal.StaleInkToken, instructions.OfType<TextInstruction>().Single().FontColor);

        string svg = Portrayal.SymbolProvider(symbols[1])!;
        Assert.Contains("sNDBCSTALE", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("sWXBARB", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_symbol_and_colour_token_resolves()
    {
        foreach (var stale in new[] { false, true })
        {
            foreach (var instruction in Portrayal.Portray(Station(Windy(), stale)))
            {
                if (instruction is PointInstruction point)
                    Assert.NotNull(Portrayal.SymbolProvider(point.SymbolReference!));
                if (instruction is TextInstruction text)
                {
                    Assert.True(Portrayal.Palette.Tokens.ContainsKey(text.FontColor));
                    Assert.True(Portrayal.Palette.Tokens.ContainsKey(text.HaloColor!));
                }
            }
        }

        Assert.Null(Portrayal.SymbolProvider("NOSUCHSYMBOL"));
    }

    [Fact]
    public void Other_features_are_not_portrayed()
    {
        var other = Station(Windy()) with { Kind = "vessel.ais.cargo" };

        Assert.False(Portrayal.CanPortray(other));
        Assert.Empty(Portrayal.Portray(other));
    }

    [Fact]
    public void The_glyph_renders_headless_in_the_barb_ink_and_grey_when_stale()
    {
        var ink = NdbcStationPortrayal.ObservationPalette.Resolve("WXBARB", PaletteType.Day);
        var grey = NdbcStationPortrayal.ObservationPalette.Resolve(NdbcStationPortrayal.StaleInkToken, PaletteType.Day);

        using var fresh = Render(Station(Windy()));
        using var stale = Render(Station(Windy(), stale: true));
        using var bare = Render(Station(Windy() with { WaveHeight = null }));
        Save(fresh, "fresh");
        Save(stale, "stale");

        // Lines are thin and anti-aliased, so judge by the darkest pixel and
        // by how many pixels are drawn at all.
        int freshDrawn = CountDrawn(fresh);
        Assert.True(freshDrawn > 40, $"Expected the glyph to be drawn, found {freshDrawn} pixels.");
        Assert.InRange(Darkest(fresh), 0, Luminance(ink) + 30);

        Assert.True(CountDrawn(stale) > 40, "Expected the stale glyph to be drawn.");
        Assert.InRange(Darkest(stale), Luminance(grey) - 30, Luminance(grey) + 30);

        // The wave height label is part of the glyph.
        Assert.True(CountDrawn(bare) < freshDrawn - 20, "Expected fewer pixels without the wave height label.");

        // The barb points into the wind (from 230°), south-west of the station:
        // with the label gone, the ink is left of the station, not right of it.
        var (left, right) = CountSides(bare);
        Assert.True(left > right, $"Expected the barb west of the station ({left} left, {right} right).");
    }

    private static SKBitmap Render(DynamicFeature feature)
    {
        var instructions = Portrayal.Portray(feature);
        var (lat, lon) = feature.Coordinates[0];
        var viewport = new Viewport
        {
            MinLatitude = lat - 0.05,
            MaxLatitude = lat + 0.05,
            MinLongitude = lon - 0.0675,
            MaxLongitude = lon + 0.0675,
            WidthPixels = 200,
            HeightPixels = 200,
            ScaleDenominator = 200_000,
        };

        return HeadlessVectorRenderer.Render(
            instructions,
            new DynamicFeatureGeometryProvider([feature]),
            Portrayal.Palette.GetColorPalette(PaletteType.Day),
            Portrayal.SymbolProvider,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0,
            widthPixels: 200,
            heightPixels: 200,
            background: RgbaColor.FromHex("#FFFFFF"),
            viewport: viewport);
    }

    // Set NDBC_RENDER_DIR to look at the glyphs.
    private static void Save(SKBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("NDBC_RENDER_DIR") is not { Length: > 0 } dir)
            return;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(dir, $"ndbc-{name}.png"));
        data.SaveTo(file);
    }

    private static double Luminance(RgbaColor c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static double Luminance(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

    private static bool IsDrawn(SKColor pixel) => Luminance(pixel) < 235;

    private static int CountDrawn(SKBitmap bitmap)
    {
        int n = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (IsDrawn(bitmap.GetPixel(x, y)))
                    n++;
        return n;
    }

    private static double Darkest(SKBitmap bitmap)
    {
        double min = 255;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                min = Math.Min(min, Luminance(bitmap.GetPixel(x, y)));
        return min;
    }

    private static (int Left, int Right) CountSides(SKBitmap bitmap)
    {
        int left = 0, right = 0, cx = bitmap.Width / 2;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (IsDrawn(bitmap.GetPixel(x, y)))
                {
                    if (x < cx - 4) left++;
                    else if (x > cx + 4) right++;
                }
        return (left, right);
    }
}
