using EncDotNet.S100.DataModel;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.DynamicSources.Nmea.Tests;

public sealed class OwnShipWeatherPortrayalTests
{
    private static readonly OwnShipWeatherPortrayal Portrayal = OwnShipWeatherPortrayal.Instance;

    internal static DynamicFeature Feature(OwnShipWeather weather, double latitude = 42.35, double longitude = -70.65) => new()
    {
        Id = NmeaWeatherSource.FeatureId,
        Kind = NmeaWeatherSource.FeatureKind,
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(latitude, longitude)],
        Attributes = new Dictionary<string, object?> { [NmeaWeatherSource.WeatherAttribute] = weather },
        LastUpdated = weather.Time,
    };

    internal static OwnShipWeather TrueWind() => new()
    {
        Time = NmeaWeatherSourceTests.Start,
        TrueWind = new WindVector(Angle.FromDegrees(200), Speed.FromKnots(14)),
        TrueWindSource = TrueWindSource.DerivedOverGround,
        RelativeWindAngle = Angle.FromDegrees(83),
        RelativeWindSpeed = Speed.FromKnots(13.8),
        RelativeWindFromDirection = Angle.FromDegrees(173),
        Heading = Angle.FromDegrees(90),
        WaterTemperatureCelsius = 12.4,
    };

    internal static OwnShipWeather RelativeOnly(bool withHeading) => new()
    {
        Time = NmeaWeatherSourceTests.Start,
        RelativeWindAngle = Angle.FromDegrees(83),
        RelativeWindSpeed = Speed.FromKnots(13.8),
        RelativeWindFromDirection = withHeading ? Angle.FromDegrees(173) : null,
        Heading = withHeading ? Angle.FromDegrees(90) : null,
    };

    [Fact]
    public void True_wind_is_a_ring_and_a_barb_next_to_own_ship_with_wind_and_water_labels()
    {
        var instructions = Portrayal.Portray(Feature(TrueWind()));

        Assert.Equal(4, instructions.Count);
        var ring = Assert.IsType<PointInstruction>(instructions[0]);
        Assert.Equal(OwnShipWeatherPortrayal.StationSymbol, ring.SymbolReference);
        Assert.Equal(OwnShipWeatherPortrayal.GlyphOffsetX, ring.LocalOffsetX);
        Assert.Equal(OwnShipWeatherPortrayal.GlyphOffsetY, ring.LocalOffsetY);

        var barb = Assert.IsType<PointInstruction>(instructions[1]);
        Assert.Equal("WXBARBN015", barb.SymbolReference); // 14 kn rounds to the 15 kn barb
        Assert.Equal(200, barb.Rotation);
        Assert.Equal(SymbolRotationCrs.Geographic, barb.RotationCrs);
        Assert.Equal(ring.LocalOffsetX, barb.LocalOffsetX);
        Assert.Equal(ring.LocalOffsetY, barb.LocalOffsetY);
        Assert.True(barb.DrawingPriority > ring.DrawingPriority);

        var texts = instructions.OfType<TextInstruction>().ToArray();
        Assert.Equal(["200° 14 kn", "Water 12.4 °C"], texts.Select(t => t.Text));
        Assert.All(texts, t => Assert.Equal("WXBARB", t.FontColor));
        Assert.True(texts[1].OffsetY > texts[0].OffsetY);
        Assert.All(instructions, i => Assert.Equal(NmeaWeatherSource.FeatureId, i.FeatureReference));
    }

    [Theory]
    [InlineData(-33.86, "WXBARBS015")] // off Sydney
    [InlineData(-0.01, "WXBARBS015")]
    [InlineData(0.0, "WXBARBN015")]    // the equator is northern
    [InlineData(42.35, "WXBARBN015")]
    public void Own_ship_south_of_the_equator_gets_a_southern_barb(double latitude, string expected)
    {
        var instructions = Portrayal.Portray(Feature(TrueWind(), latitude: latitude, longitude: 151.2));

        var barb = Assert.IsType<PointInstruction>(instructions[1]);
        Assert.Equal(expected, barb.SymbolReference);
        Assert.NotNull(Portrayal.SymbolProvider(barb.SymbolReference!));
    }

    [Theory]
    [InlineData(-33.86, "WXBARBS015")]
    [InlineData(0.0, "WXBARBN015")]
    public void Relative_wind_south_of_the_equator_is_a_grey_southern_barb(double latitude, string expected)
    {
        var instructions = Portrayal.Portray(Feature(RelativeOnly(withHeading: true), latitude: latitude, longitude: 151.2));

        var barb = Assert.IsType<PointInstruction>(instructions[1]);
        Assert.Equal(OwnShipWeatherPortrayal.RelativeSymbolPrefix + expected, barb.SymbolReference);
        string svg = Portrayal.SymbolProvider(barb.SymbolReference!)!;
        Assert.Contains("<title>" + expected + "</title>", svg, StringComparison.Ordinal);
        Assert.Contains("sOWNWXREL", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("sWXBARB", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("fWXBARB", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_southern_barb_renders_with_its_feathers_on_the_other_side()
    {
        // Wind from the north: the staff runs straight up from the ring. A
        // northern barb's feathers are on its right (clockwise) side, a
        // southern one's on its left.
        var north = TrueWind() with { TrueWind = new WindVector(Angle.Zero, Speed.FromKnots(25)), WaterTemperatureCelsius = null };
        using var northern = Render(Feature(north, latitude: 42.35), withText: false);
        using var southern = Render(Feature(north, latitude: -33.86, longitude: 151.2), withText: false);
        Save(northern, "north-from-000");
        Save(southern, "south-from-000");

        int ringX = northern.Width / 2 + 25, ringY = northern.Height / 2 - 25;
        int Right(SKBitmap b) => CountDrawn(b, p => p.Y < ringY - 6 && p.X > ringX + 1);
        int Left(SKBitmap b) => CountDrawn(b, p => p.Y < ringY - 6 && p.X < ringX - 1);

        Assert.True(Right(northern) > Left(northern) + 5, $"Northern feathers: {Left(northern)} left, {Right(northern)} right.");
        Assert.True(Left(southern) > Right(southern) + 5, $"Southern feathers: {Left(southern)} left, {Right(southern)} right.");
    }

    [Fact]
    public void Relative_wind_with_a_heading_is_a_grey_barb_labelled_relative()
    {
        var instructions = Portrayal.Portray(Feature(RelativeOnly(withHeading: true)));

        var points = instructions.OfType<PointInstruction>().ToArray();
        Assert.Equal(
            [OwnShipWeatherPortrayal.RelativeSymbolPrefix + "OWNWXSTN", OwnShipWeatherPortrayal.RelativeSymbolPrefix + "WXBARBN015"],
            points.Select(p => p.SymbolReference));
        Assert.Equal(173, points[1].Rotation);

        var label = Assert.Single(instructions.OfType<TextInstruction>());
        Assert.Equal("Rel. wind 083° 14 kn", label.Text);
        Assert.Equal(OwnShipWeatherPortrayal.RelativeInkToken, label.FontColor);

        string svg = Portrayal.SymbolProvider(points[1].SymbolReference!)!;
        Assert.Contains("sOWNWXREL", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("sWXBARB", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Relative_wind_without_a_heading_has_no_barb_only_the_label()
    {
        var instructions = Portrayal.Portray(Feature(RelativeOnly(withHeading: false)));

        var ring = Assert.Single(instructions.OfType<PointInstruction>());
        Assert.Equal(OwnShipWeatherPortrayal.RelativeSymbolPrefix + "OWNWXSTN", ring.SymbolReference);
        Assert.Equal("Rel. wind 083° 14 kn", Assert.Single(instructions.OfType<TextInstruction>()).Text);
    }

    [Fact]
    public void A_calm_is_an_unrotated_circle_and_temperatures_alone_are_a_ring_and_label()
    {
        var calm = Portrayal.Portray(Feature(TrueWind() with { TrueWind = new WindVector(Angle.Zero, Speed.FromKnots(1)) }));
        var circle = Assert.IsType<PointInstruction>(calm[1]);
        Assert.Equal("WXCALM", circle.SymbolReference);
        Assert.Null(circle.Rotation);

        var waterOnly = new OwnShipWeather { Time = NmeaWeatherSourceTests.Start, WaterTemperatureCelsius = 12.4 };
        var instructions = Portrayal.Portray(Feature(waterOnly));
        Assert.Single(instructions.OfType<PointInstruction>());
        Assert.Equal("Water 12.4 °C", Assert.Single(instructions.OfType<TextInstruction>()).Text);
    }

    [Fact]
    public void Every_symbol_and_colour_token_resolves()
    {
        foreach (var weather in new[] { TrueWind(), RelativeOnly(true), RelativeOnly(false) })
        {
            foreach (var instruction in Portrayal.Portray(Feature(weather)))
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
        Assert.Null(Portrayal.SymbolProvider(OwnShipWeatherPortrayal.RelativeSymbolPrefix + "NOSUCHSYMBOL"));
    }

    [Fact]
    public void Other_features_are_not_portrayed()
    {
        var other = Feature(TrueWind()) with { Kind = "ownship" };

        Assert.False(Portrayal.CanPortray(other));
        Assert.Empty(Portrayal.Portray(other));
        Assert.Empty(Portrayal.Portray(Feature(TrueWind()) with { Attributes = new Dictionary<string, object?>() }));
    }

    [Fact]
    public void The_own_ship_wind_barb_renders_headless_beside_own_ship_and_into_the_wind()
    {
        var ink = OwnShipWeatherPortrayal.WeatherInkPalette.Resolve("WXBARB", PaletteType.Day);
        var grey = OwnShipWeatherPortrayal.WeatherInkPalette.Resolve(OwnShipWeatherPortrayal.RelativeInkToken, PaletteType.Day);

        // Without labels, so only the ring and barb are drawn.
        var noLabels = TrueWind() with { WaterTemperatureCelsius = null };
        using var fresh = Render(Feature(noLabels), withText: false);
        using var relative = Render(Feature(RelativeOnly(withHeading: true)), withText: false);
        using var labelled = Render(Feature(TrueWind()), withText: true);
        Save(fresh, "true");
        Save(relative, "relative");
        Save(labelled, "labelled");

        int drawn = CountDrawn(fresh, _ => true);
        Assert.True(drawn > 30, $"Expected the barb to be drawn, found {drawn} pixels.");
        Assert.InRange(Darkest(fresh), 0, Luminance(ink) + 30);
        Assert.InRange(Darkest(relative), Luminance(grey) - 30, Luminance(grey) + 30);

        // Own ship is the centre: the glyph leaves room for its symbol...
        int cx = fresh.Width / 2, cy = fresh.Height / 2;
        Assert.Equal(0, CountDrawn(fresh, p => Math.Abs(p.X - cx) <= 8 && Math.Abs(p.Y - cy) <= 8));

        // ...and sits to its upper right, 8 mm (25 px) right and up.
        int ringX = cx + 25, ringY = cy - 25;
        int nearRing = CountDrawn(fresh, p => Math.Abs(p.X - ringX) <= 4 && Math.Abs(p.Y - ringY) <= 4);
        Assert.True(nearRing > 5, $"Expected the ring at ({ringX}, {ringY}), found {nearRing} pixels.");

        // The wind is from 200°, so the staff runs from the ring down and a
        // little left (south-south-west), never up or right of it.
        int below = CountDrawn(fresh, p => p.Y > ringY + 5);
        int above = CountDrawn(fresh, p => p.Y < ringY - 5);
        int right = CountDrawn(fresh, p => p.X > ringX + 6);
        Assert.True(below > 10, $"Expected the staff below the ring, found {below} pixels.");
        Assert.True(above + right < below / 2, $"Expected nothing much above ({above}) or right ({right}) of the ring.");

        // The labels draw to the right of the ring.
        Assert.True(CountDrawn(labelled, p => p.X > ringX + 6) > right + 20, "Expected the labels right of the ring.");
    }

    private static SKBitmap Render(DynamicFeature feature, bool withText)
    {
        var instructions = Portrayal.Portray(feature).Where(i => withText || i is not TextInstruction).ToArray();
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

    // Set NMEA_RENDER_DIR to look at the glyphs.
    private static void Save(SKBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("NMEA_RENDER_DIR") is not { Length: > 0 } dir)
            return;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(dir, $"ownship-weather-{name}.png"));
        data.SaveTo(file);
    }

    private static double Luminance(RgbaColor c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static double Luminance(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

    private static int CountDrawn(SKBitmap bitmap, Func<(int X, int Y), bool> where)
    {
        int n = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (Luminance(bitmap.GetPixel(x, y)) < 235 && where((x, y)))
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
}
