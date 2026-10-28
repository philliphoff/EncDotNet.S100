using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Tests for <see cref="WindBarb"/>, the <see cref="WindBarbStyle"/> bands and
/// the generated barb symbols: feather counts, the 5 kn rounding, the calm
/// threshold, and the staff pointing into the wind (#1068); the mirrored
/// southern-hemisphere set and <see cref="WindBarbSymbols"/> (#1072).
/// </summary>
public class WindBarbTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 0, 0, 1)]
    [InlineData(10, 0, 1, 0)]
    [InlineData(15, 0, 1, 1)]
    [InlineData(25, 0, 2, 1)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(65, 1, 1, 1)]
    [InlineData(115, 2, 1, 1)]
    [InlineData(200, 4, 0, 0)]
    public void Speeds_decompose_into_pennants_full_and_half_barbs(int knots, int pennants, int full, int half)
    {
        var barb = WindBarb.FromKnots(knots);

        Assert.Equal(knots, barb.Knots);
        Assert.Equal(pennants, barb.Pennants);
        Assert.Equal(full, barb.FullBarbs);
        Assert.Equal(half, barb.HalfBarbs);
        Assert.Equal(knots, 50 * barb.Pennants + 10 * barb.FullBarbs + 5 * barb.HalfBarbs);
        Assert.Equal(knots == 0, barb.IsCalm);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(2.49, 0)]
    [InlineData(2.5, 5)]
    [InlineData(7.49, 5)]
    [InlineData(7.5, 10)]
    [InlineData(63.0, 65)]
    [InlineData(197.4, 195)]
    [InlineData(197.5, 200)]
    [InlineData(260.0, 200)]
    public void Speeds_round_to_the_nearest_5_knots_and_calm_below_2_5(double knots, int expected)
    {
        Assert.Equal(expected, WindBarb.FromKnots(knots).Knots);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Negative_and_NaN_speeds_are_rejected(double knots)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindBarb.FromKnots(knots));
    }

    [Fact]
    public void FromSpeed_converts_metres_per_second_to_knots()
    {
        // 25 m/s is 48.6 kn: the 50 kn pennant.
        var barb = WindBarb.FromSpeed(Speed.FromMetresPerSecond(25));

        Assert.Equal(50, barb.Knots);
        Assert.Equal(1, barb.Pennants);
    }

    [Fact]
    public void There_are_41_barbs_from_calm_to_200_knots()
    {
        var all = WindBarb.All;

        Assert.Equal(41, all.Count);
        Assert.True(all[0].IsCalm);
        Assert.Equal(WindBarb.CalmSymbol, all[0].SymbolName);
        Assert.Equal("WXBARBN005", all[1].SymbolName);
        Assert.Equal("WXBARBN200", all[^1].SymbolName);
    }

    [Theory]
    [InlineData(0, "WXCALM")]
    [InlineData(5, "WXBARBN005")]
    [InlineData(10, "WXBARBN010")]
    [InlineData(15, "WXBARBN015")]
    [InlineData(25, "WXBARBN025")]
    [InlineData(50, "WXBARBN050")]
    [InlineData(65, "WXBARBN065")]
    [InlineData(115, "WXBARBN115")]
    public void Style_resolves_sample_speeds_in_metres_per_second_to_their_barb(int knots, string symbol)
    {
        var scheme = new WindBarbStyle().ResolveSymbolScheme(MarinerSettings.Default);
        float metresPerSecond = (float)Speed.FromKnots(knots).TotalMetresPerSecond;

        var band = scheme.Resolve(metresPerSecond, latitude: 50);

        Assert.NotNull(band);
        Assert.Equal(symbol, band.SymbolRef);
        Assert.Equal(1.0, band.ScaleFor(metresPerSecond));
    }

    [Fact]
    public void Style_bands_are_contiguous_from_calm_and_open_above_200_knots()
    {
        var bands = WindBarbStyle.Bands;

        Assert.Equal(41, bands.Count);
        Assert.Equal(0f, bands[0].MinValue);
        Assert.Equal((float)Speed.FromKnots(2.5).TotalMetresPerSecond, bands[0].MaxValue);
        for (int i = 1; i < bands.Count; i++)
            Assert.Equal(bands[i - 1].MaxValue, bands[i].MinValue);
        Assert.Equal(float.PositiveInfinity, bands[^1].MaxValue);
        Assert.Equal("WXBARBN200", new WindBarbStyle().ResolveSymbolScheme(MarinerSettings.Default).Resolve(150f, 0)?.SymbolRef);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(270f)]
    [InlineData(359f)]
    public void Barbs_are_rotated_by_the_from_direction_unchanged(float fromDirection)
    {
        // The barb's staff is drawn up from the station, so the renderers'
        // clockwise rotation by the "from" direction points it into the wind.
        var scheme = new WindBarbStyle().ResolveSymbolScheme(MarinerSettings.Default);

        Assert.Equal(0, scheme.RotationOffsetDegrees);
        Assert.Equal(fromDirection, scheme.RotationFor(fromDirection));
    }

    [Theory]
    [InlineData(270f, -1, 0)]   // westerly: staff west of the station
    [InlineData(0f, 0, -1)]     // northerly: staff north (screen up)
    [InlineData(90f, 1, 0)]     // easterly: staff east
    [InlineData(180f, 0, 1)]    // southerly: staff south (screen down)
    public void A_rotated_barb_staff_points_toward_where_the_wind_comes_from(float fromDirection, int expectedX, int expectedY)
    {
        // The staff tip in the symbol, rotated as SkiaCoverageArrowRenderer
        // (canvas.RotateDegrees, clockwise on a y-down screen) and Mapsui
        // (SymbolRotation, clockwise) rotate it.
        var (tipX, tipY) = StaffTip(Svg("WXBARBN025"));
        double rotation = new WindBarbStyle().ResolveSymbolScheme(MarinerSettings.Default).RotationFor(fromDirection) * Math.PI / 180;
        double x = tipX * Math.Cos(rotation) - tipY * Math.Sin(rotation);
        double y = tipX * Math.Sin(rotation) + tipY * Math.Cos(rotation);

        Assert.Equal(expectedX, Math.Sign(Math.Round(x, 6)));
        Assert.Equal(expectedY, Math.Sign(Math.Round(y, 6)));
    }

    [Theory]
    [InlineData(5, 0, 0, 1)]
    [InlineData(10, 0, 1, 0)]
    [InlineData(15, 0, 1, 1)]
    [InlineData(25, 0, 2, 1)]
    [InlineData(50, 1, 0, 0)]
    [InlineData(65, 1, 1, 1)]
    [InlineData(115, 2, 1, 1)]
    [InlineData(195, 3, 4, 1)]
    public void Barb_symbols_carry_the_feathers_of_their_speed(int knots, int pennants, int full, int half)
    {
        var svg = Svg(WindBarb.FromKnots(knots).SymbolName);
        var ids = svg.Descendants().Select(e => (string?)e.Attribute("id")).Where(id => id is not null).ToArray();

        Assert.Equal(pennants, ids.Count(id => Regex.IsMatch(id!, "^pennant[0-9]+$")));
        Assert.Equal(full, ids.Count(id => Regex.IsMatch(id!, "^barb[0-9]+$")));
        Assert.Equal(half, ids.Count(id => id == "half"));
        Assert.Contains("staff", ids);
    }

    [Fact]
    public void Barb_feathers_sit_on_the_clockwise_side_at_the_outer_end()
    {
        // Northern hemisphere: with the staff pointing up, the feathers are to
        // its right (+x), and every feather is nearer the tip than the station.
        var svg = Svg("WXBARBN065");
        var (_, tipY) = StaffTip(svg);
        var feathers = svg.Descendants().Where(e => ((string?)e.Attribute("id"))?.StartsWith("pennant", StringComparison.Ordinal) == true
            || (string?)e.Attribute("id") is "barb1" or "half");

        Assert.All(feathers, feather =>
        {
            var points = Points((string)feather.Attribute("d")!);
            Assert.All(points, p => Assert.True(p.X >= 0, $"{feather.Attribute("id")} has a point left of the staff."));
            Assert.True(points.Max(p => p.X) > 1, $"{feather.Attribute("id")} doesn't reach out from the staff.");
            Assert.True(points.Max(p => p.Y) < tipY / 2, $"{feather.Attribute("id")} is nearer the station than the tip.");
        });
    }

    [Fact]
    public void A_lone_half_barb_sits_in_from_the_tip()
    {
        var svg = Svg("WXBARBN005");
        var (_, tipY) = StaffTip(svg);
        var half = Points((string)svg.Descendants().Single(e => (string?)e.Attribute("id") == "half").Attribute("d")!);

        Assert.True(half[0].Y > tipY + 0.5, "A lone half barb is drawn at the tip.");
    }

    [Fact]
    public void Calm_is_a_circle_with_no_staff()
    {
        var svg = Svg(WindBarb.CalmSymbol);

        Assert.DoesNotContain(svg.Descendants(), e => (string?)e.Attribute("id") == "staff");
        Assert.Contains(svg.Descendants(), e => e.Name.LocalName == "circle" && (string?)e.Attribute("class") == "f0 sWXBARB");
    }

    [Fact]
    public void Every_symbol_is_an_embedded_S100_svg_centred_on_its_pivot()
    {
        var expected = WindBarb.All.Select(b => b.SymbolName)
            .Concat(WindBarb.All.Where(b => !b.IsCalm).Select(b => b.SouthernSymbolName))
            .Append(WaveDirectionStyle.ArrowSymbol)
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, WeatherCoverageStyle.Symbols.Names.Order(StringComparer.Ordinal));

        Assert.All(WeatherCoverageStyle.Symbols.Names, name =>
        {
            var svg = Svg(name);
            Assert.Contains(svg.Descendants(), e => (string?)e.Attribute("class") == "pivotPoint layout");
            Assert.Contains(svg.Descendants(), e => (string?)e.Attribute("class") == "symbolBox layout");
            Assert.EndsWith("mm", (string)svg.Attribute("width")!, StringComparison.Ordinal);

            // The renderers centre the box on the cell, so the pivot (0, 0) is its centre.
            var box = ((string)svg.Attribute("viewBox")!).Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(0, box[0] + box[2] / 2, 9);
            Assert.Equal(0, box[1] + box[3] / 2, 9);
        });
    }

    // ---------------------------------------------------------------
    // Southern hemisphere (#1072)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(0, -30.0, "WXCALM")]
    [InlineData(25, -30.0, "WXBARBS025")]
    [InlineData(65, -0.01, "WXBARBS065")]
    [InlineData(65, 0.0, "WXBARBN065")]
    [InlineData(65, 45.0, "WXBARBN065")]
    [InlineData(250, -60.0, "WXBARBS200")]
    public void The_symbol_helper_picks_the_hemisphere_by_latitude(double knots, double latitude, string expected)
    {
        Assert.Equal(expected, WindBarbSymbols.For(knots, latitude));
        Assert.Equal(expected, WindBarbSymbols.For(Speed.FromKnots(knots), latitude));
        Assert.NotNull(WeatherCoverageStyle.Symbols.GetSymbol(expected));
    }

    [Theory]
    [InlineData(-33.9, "WXBARBS025")]
    [InlineData(-0.5, "WXBARBS025")]
    [InlineData(0.0, "WXBARBN025")]
    [InlineData(51.5, "WXBARBN025")]
    public void Style_resolves_a_cell_to_the_barb_of_its_hemisphere(double latitude, string expected)
    {
        var scheme = new WindBarbStyle().ResolveSymbolScheme(MarinerSettings.Default);
        float metresPerSecond = (float)Speed.FromKnots(25).TotalMetresPerSecond;

        Assert.Equal(expected, scheme.Resolve(metresPerSecond, latitude)?.SymbolRef);
        // The helper and the style agree.
        Assert.Equal(expected, WindBarbSymbols.For(25, latitude));
    }

    [Fact]
    public void Southern_bands_mirror_the_northern_ranges()
    {
        var north = WindBarbStyle.Bands;
        var south = WindBarbStyle.SouthernBands;

        Assert.Equal(north.Count, south.Count);
        for (int i = 0; i < north.Count; i++)
        {
            Assert.Equal(north[i].MinValue, south[i].MinValue);
            Assert.Equal(north[i].MaxValue, south[i].MaxValue);
            Assert.Equal(north[i].Label, south[i].Label);
            Assert.Equal(north[i].SymbolRef.Replace(WindBarb.NorthernSymbolPrefix, WindBarb.SouthernSymbolPrefix, StringComparison.Ordinal), south[i].SymbolRef);
        }

        Assert.Equal(WindBarb.CalmSymbol, south[0].SymbolRef);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(65)]
    [InlineData(195)]
    public void A_southern_barb_is_the_mirror_image_of_its_northern_barb(int knots)
    {
        var barb = WindBarb.FromKnots(knots);
        var north = Svg(barb.SymbolName);
        var south = Svg(barb.SouthernSymbolName);

        var northPaths = north.Descendants().Where(e => e.Name.LocalName == "path").ToArray();
        var southPaths = south.Descendants().Where(e => e.Name.LocalName == "path").ToArray();
        Assert.Equal(northPaths.Select(e => (string?)e.Attribute("id")), southPaths.Select(e => (string?)e.Attribute("id")));

        for (int i = 0; i < northPaths.Length; i++)
        {
            var n = Points((string)northPaths[i].Attribute("d")!);
            var s = Points((string)southPaths[i].Attribute("d")!);
            Assert.Equal(n.Length, s.Length);
            for (int j = 0; j < n.Length; j++)
            {
                Assert.Equal(-n[j].X, s[j].X, 9);
                Assert.Equal(n[j].Y, s[j].Y, 9);
            }
        }

        // Feathers on the anticlockwise (-x) side.
        Assert.All(
            southPaths.Where(e => (string?)e.Attribute("id") != "staff"),
            feather => Assert.All(Points((string)feather.Attribute("d")!), p => Assert.True(p.X <= 0)));
        Assert.Contains("southern hemisphere", south.ToString(), StringComparison.Ordinal);
    }

    internal static XElement Svg(string name)
    {
        var text = WeatherCoverageStyle.Symbols.GetSymbol(name);
        Assert.NotNull(text);
        return XElement.Parse(text);
    }

    private static (double X, double Y) StaffTip(XElement svg)
    {
        var staff = svg.Descendants().Single(e => (string?)e.Attribute("id") == "staff");
        var points = Points((string)staff.Attribute("d")!);
        Assert.Equal((0, 0), points[0]);
        return points[1];
    }

    private static (double X, double Y)[] Points(string path)
    {
        var numbers = Regex.Matches(path, @"-?[0-9]+(\.[0-9]+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return Enumerable.Range(0, numbers.Length / 2).Select(i => (numbers[2 * i], numbers[2 * i + 1])).ToArray();
    }
}
