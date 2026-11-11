using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.TropicalCyclones.Tests;

/// <summary>
/// Issue #1294: an archived NHC advisory's zips load into the model with
/// every forecast point, the cone, radii by quadrant and the past track; and
/// the reader copes with missing radii, a final advisory with no cone, and
/// the southern hemisphere.
/// </summary>
public sealed class NhcAdvisoryReaderTests : IDisposable
{
    private static readonly DateTime Synoptic = new(2022, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("tc-reader-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Ian_advisory_20_reads_the_storm_and_the_advisory()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);

        Assert.Equal("AL092022", advisory.Storm.AtcfId);
        Assert.Equal("Ian", advisory.Storm.Name);
        Assert.Equal("AL", advisory.Storm.Basin);
        Assert.Equal("North Atlantic", advisory.Storm.BasinName);
        Assert.Equal(9, advisory.Storm.Number);
        Assert.Equal(2022, advisory.Storm.Year);
        Assert.Equal("20", advisory.Advisory);
        Assert.Equal(new DateTimeOffset(2022, 9, 27, 23, 0, 0, TimeSpan.FromHours(-4)), advisory.IssueTime);
        Assert.Equal(new DateTime(2022, 9, 28, 3, 0, 0, DateTimeKind.Utc), advisory.IssueTime.UtcDateTime);
        Assert.Equal(120, advisory.ForecastPeriodHours);
    }

    [Fact]
    public void Ian_advisory_20_reads_every_forecast_point()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);

        Assert.Equal([0, 12, 24, 36, 48, 60, 72, 96], advisory.Forecast.Select(p => p.Tau));
        Assert.Equal(Synoptic, advisory.SynopticTime);
        Assert.All(advisory.Forecast, p => Assert.Equal(Synoptic.AddHours(p.Tau), p.ValidTime));
        Assert.All(advisory.Forecast, p => Assert.Equal(DateTimeKind.Utc, p.ValidTime.Kind));
        Assert.Equal(new DateTime(2022, 10, 2, 0, 0, 0, DateTimeKind.Utc), advisory.ForecastEnd);

        var now = advisory.Current!;
        Assert.Equal(24.9, now.Position.Latitude, 6);
        Assert.Equal(-82.9, now.Position.Longitude, 6);
        Assert.Equal(105, now.MaxWindKnots);
        Assert.Equal(130, now.GustKnots);
        Assert.Equal(952, now.PressureHectopascals);
        Assert.Equal(TropicalCycloneClass.MajorHurricane, now.Classification);
        Assert.Equal("MH", now.ClassCode);
        Assert.Equal("M", now.ClassLetter);
        Assert.Equal("Major Hurricane", now.ClassDescription);
        Assert.Equal(3, now.SaffirSimpsonCategory);
        Assert.Equal(15, now.MotionDirectionDegrees);
        Assert.Equal(9, now.MotionSpeedKnots);

        // NHC writes 9999 for what it doesn't forecast.
        var later = advisory.Forecast[1];
        Assert.Null(later.PressureHectopascals);
        Assert.Null(later.MotionDirectionDegrees);
        Assert.Equal(115, later.MaxWindKnots);
        Assert.Equal(115, advisory.PeakForecastWindKnots);

        Assert.Equal(
            [TropicalCycloneClass.MajorHurricane, TropicalCycloneClass.MajorHurricane, TropicalCycloneClass.MajorHurricane,
             TropicalCycloneClass.TropicalStorm, TropicalCycloneClass.TropicalStorm, TropicalCycloneClass.TropicalStorm,
             TropicalCycloneClass.TropicalDepression, TropicalCycloneClass.SubtropicalDepression],
            advisory.Forecast.Select(p => p.Classification));
        Assert.Equal(8, advisory.ForecastTrack.Count);
    }

    [Fact]
    public void Ian_advisory_20_reads_the_cone_split_at_72_hours()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);

        var cone = Assert.IsType<TropicalCycloneCone>(advisory.Cone);
        var surface = Assert.Single(cone.Surfaces);
        Assert.Equal(1376, surface.ExteriorRing.Count);
        Assert.NotEmpty(cone.DaysOneToThree);
        Assert.NotEmpty(cone.DaysFourToFive);

        // Days 1-3 hold the storm now and at 48 h; days 4-5 the 96 h point.
        Assert.True(Contains(cone.DaysOneToThree, advisory.Forecast[0].Position));
        Assert.True(Contains(cone.DaysOneToThree, advisory.Forecast.Single(p => p.Tau == 48).Position));
        Assert.True(Contains(cone.DaysFourToFive, advisory.Forecast.Single(p => p.Tau == 96).Position));
        Assert.False(Contains(cone.DaysFourToFive, advisory.Forecast[0].Position));

        // The two parts make up the cone.
        var whole = Area(cone.Surfaces);
        Assert.Equal(whole, Area(cone.DaysOneToThree) + Area(cone.DaysFourToFive), whole * 1e-6);
    }

    [Fact]
    public void Ian_advisory_20_reads_the_radii_by_quadrant()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);

        var now = advisory.Forecast[0].Radii;
        Assert.Equal([34, 50, 64], now.Select(r => r.ThresholdKnots));
        Assert.Equal((120.0, 120.0, 100.0, 120.0), Quadrants(now[0]));
        Assert.Equal((60.0, 60.0, 50.0, 60.0), Quadrants(now[1]));
        Assert.Equal((35.0, 35.0, 20.0, 20.0), Quadrants(now[2]));
        Assert.Equal(120, now[0].MaximumNauticalMiles);
        Assert.All(now, r => Assert.Equal(361, Assert.Single(r.Surfaces).ExteriorRing.Count));

        var thresholds = advisory.Forecast.ToDictionary(p => p.Tau, p => p.Radii.Select(r => r.ThresholdKnots).ToArray());
        Assert.Equal([34, 50, 64], thresholds[24]);
        Assert.Equal([34, 50], thresholds[36]);
        Assert.Equal([34], thresholds[60]);
        Assert.Empty(thresholds[72]);
        Assert.Empty(thresholds[96]);
        Assert.Equal((280.0, 120.0, 140.0, 170.0), Quadrants(advisory.Forecast.Single(p => p.Tau == 36).Radii[0]));
    }

    [Fact]
    public void Ian_advisory_20_reads_the_past_track_up_to_the_advisory()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);

        var track = advisory.PastTrack;
        Assert.NotEmpty(track);
        Assert.Equal(new DateTime(2022, 9, 22, 18, 0, 0, DateTimeKind.Utc), track[0].Time);
        Assert.Equal(TropicalCycloneClass.Low, track[0].Classification);
        Assert.Equal("INVEST", track[0].Name);
        Assert.Equal(Synoptic, track[^1].Time);
        Assert.All(track, p => Assert.True(p.Time <= Synoptic));
        Assert.True(track.Zip(track.Skip(1)).All(pair => pair.First.Time < pair.Second.Time));
        Assert.Equal(track[0].Time, advisory.TrackStart);

        // The archived best track goes on to 1 October; those points aren't "past" for advisory 20.
        Assert.DoesNotContain(track, p => p.Time.Day == 29);

        // Observed radii come with the points that have them.
        Assert.Contains(track, p => p.Radii.Count > 0);
        Assert.Contains(track, p => p.Classification == TropicalCycloneClass.Hurricane && p.SaffirSimpsonCategory >= 1);
        var last = track[^1];
        Assert.Equal(TropicalCycloneClass.Hurricane, last.Classification);
        Assert.NotNull(last.MaxWindKnots);
    }

    [Fact]
    public void Ian_reads_the_same_from_its_folder()
    {
        var copy = TropicalCycloneFixtures.CopyIan(Path.Combine(_dir, "ian"));

        var fromFolder = NhcAdvisoryReader.Read(Path.GetDirectoryName(copy)!);
        var fromZip = NhcAdvisoryReader.Read(copy);

        Assert.Equal("20", fromFolder.Advisory);
        Assert.Equal(fromZip.Forecast.Count, fromFolder.Forecast.Count);
        Assert.Equal(fromZip.PastTrack.Count, fromFolder.PastTrack.Count);
        Assert.Equal(3, fromFolder.Forecast[0].Radii.Count);
    }

    [Fact]
    public void A_forecast_zip_alone_has_no_radii_or_past_track()
    {
        var alone = Path.Combine(_dir, "al092022_5day_020.zip");
        File.Copy(TropicalCycloneFixtures.IanForecast, alone);

        var advisory = NhcAdvisoryReader.Read(alone);

        Assert.Equal(8, advisory.Forecast.Count);
        Assert.NotNull(advisory.Cone);
        Assert.All(advisory.Forecast, p => Assert.Empty(p.Radii));
        Assert.Empty(advisory.PastTrack);
        Assert.Equal(advisory.SynopticTime, advisory.TrackStart);
    }

    [Fact]
    public void Radii_with_every_quadrant_zero_are_left_out()
    {
        // A depression: NHC writes a 34 kt row of zeros.
        var forecast = SyntheticNhc.ForecastZip("AL022023", "004", "1000 AM CDT Fri Jun 02 2023", "Two",
        [
            new(0, "02/1200", 26.7, -86.3, 30, 40, "TD", "D", 1002),
            new(12, "03/0000", 25.6, -86.2, 30, 40, "TD", "D"),
        ], SyntheticNhc.Square(26, -86, 1.5));
        var radii = SyntheticNhc.WindRadiiZip("AL022023", "004", "2023060215",
        [
            new(0, "2023060215", 34, 0, 0, 0, 0, 26.7, -86.3),
        ]);

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), new MemoryStream(radii), null);

        Assert.All(advisory.Forecast, p => Assert.Empty(p.Radii));
        Assert.Equal(new DateTimeOffset(2023, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5)), advisory.IssueTime);
        Assert.Equal(new DateTime(2023, 6, 2, 12, 0, 0, DateTimeKind.Utc), advisory.SynopticTime);
    }

    [Fact]
    public void Radii_missing_for_some_forecast_times_leave_those_points_without()
    {
        var forecast = SyntheticNhc.ForecastZip("EP052024", "010", "200 PM PDT Mon Jul 01 2024", "Emilia",
        [
            new(0, "01/1800", 15, -110, 70, 85, "HU", "H", 985),
            new(12, "02/0600", 16, -112, 60, 75, "TS", "S"),
            new(24, "02/1800", 17, -114, 45, 55, "TS", "S"),
        ], SyntheticNhc.Square(16, -112, 3));
        var radii = SyntheticNhc.WindRadiiZip("EP052024", "010", "2024070121",
        [
            new(0, "2024070121", 34, 60, 50, 40, 50, 15, -110),
            new(0, "2024070121", 64, 15, 10, 0, 10, 15, -110),
            new(24, "2024070218", 34, 40, 30, 0, 0, 17, -114),
        ]);

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), new MemoryStream(radii), null);

        Assert.Equal([34, 64], advisory.Forecast[0].Radii.Select(r => r.ThresholdKnots));
        Assert.Empty(advisory.Forecast[1].Radii);
        var late = Assert.Single(advisory.Forecast[2].Radii);
        Assert.Equal((40.0, 30.0, 0.0, 0.0), Quadrants(late));
        Assert.False(late.IsEmpty);
        Assert.Equal(TropicalCycloneClass.Hurricane, advisory.Forecast[0].Classification);
        Assert.Equal("EP", advisory.Storm.Basin);
    }

    [Fact]
    public void A_final_advisory_with_no_cone_reads_its_one_point()
    {
        var forecast = SyntheticNhc.ForecastZip("AL142024", "030", "500 AM AST Sat Oct 12 2024", "Leslie",
        [
            new(0, "12/0600", 35.2, -30.1, 40, 50, "PTC", "D", 1004),
        ], cone: null);

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), null, null);

        Assert.Null(advisory.Cone);
        var point = Assert.Single(advisory.Forecast);
        Assert.Equal(TropicalCycloneClass.PostTropical, point.Classification);
        Assert.Equal(1004, point.PressureHectopascals);
        Assert.Equal(advisory.SynopticTime, advisory.ForecastEnd);
        Assert.Single(advisory.ForecastTrack);
        Assert.NotNull(advisory.Extent);
        Assert.Equal(new DateTime(2024, 10, 12, 9, 0, 0, DateTimeKind.Utc), advisory.IssueTime.UtcDateTime);
    }

    [Fact]
    public void A_short_forecast_has_a_cone_but_no_days_four_and_five()
    {
        var forecast = SyntheticNhc.ForecastZip("AL022023", "006", "500 AM EDT Sat Jun 03 2023", "Arlene",
        [
            new(0, "03/0600", 25.0, -85.6, 35, 45, "TS", "S", 999),
            new(12, "03/1800", 24.4, -85.3, 30, 40, "TD", "D"),
            new(24, "04/0600", 23.9, -85.0, 25, 35, "LO", "D"),
        ], SyntheticNhc.Square(24.5, -85.3, 1.2));

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), null, null);

        var cone = Assert.IsType<TropicalCycloneCone>(advisory.Cone);
        Assert.Same(cone.Surfaces, cone.DaysOneToThree);
        Assert.Empty(cone.DaysFourToFive);
    }

    [Fact]
    public void Southern_hemisphere_coordinates_keep_their_sign_and_split_the_cone()
    {
        // A cyclone off Queensland, moving south-east (synthetic: NHC doesn't forecast there).
        var forecast = SyntheticNhc.ForecastZip("SH152025", "005", "1000 PM UTC Mon Feb 10 2025", "Alfa",
        [
            new(0, "10/1800", -15.0, 150.0, 80, 100, "HU", "H", 970),
            new(24, "11/1800", -17.0, 152.0, 90, 110, "HU", "H"),
            new(48, "12/1800", -19.0, 154.0, 90, 110, "HU", "H"),
            new(72, "13/1800", -21.0, 156.0, 80, 100, "HU", "H"),
            new(96, "14/1800", -23.0, 158.0, 60, 75, "TS", "S"),
            new(120, "15/1800", -25.0, 160.0, 45, 55, "TS", "S"),
        ], [(-13.5, 148.5), (-13.5, 151.0), (-20.0, 162.0), (-26.5, 162.0), (-26.5, 157.0), (-16.5, 148.5), (-13.5, 148.5)]);
        var radii = SyntheticNhc.WindRadiiZip("SH152025", "005", "2025021022",
        [
            new(0, "2025021022", 34, 90, 120, 100, 80, -15.0, 150.0),
            new(0, "2025021022", 50, 40, 50, 45, 35, -15.0, 150.0),
        ]);
        var bestTrack = SyntheticNhc.BestTrackZip("SH152025",
        [
            new(new DateTime(2025, 2, 10, 6, 0, 0, DateTimeKind.Utc), -13.8, 148.6, 55, "TS", "ALFA"),
            new(new DateTime(2025, 2, 10, 12, 0, 0, DateTimeKind.Utc), -14.4, 149.3, 65, "HU", "ALFA"),
            new(new DateTime(2025, 2, 10, 18, 0, 0, DateTimeKind.Utc), -15.0, 150.0, 80, "HU", "ALFA"),
            new(new DateTime(2025, 2, 11, 0, 0, 0, DateTimeKind.Utc), -15.5, 150.5, 85, "HU", "ALFA"),
        ],
        [
            new(0, "2025021012", 34, 70, 90, 80, 60, -14.4, 149.3),
        ]);

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), new MemoryStream(radii), new MemoryStream(bestTrack));

        Assert.Equal("SH", advisory.Storm.Basin);
        Assert.Equal("Southern Hemisphere", advisory.Storm.BasinName);
        Assert.Equal(-15.0, advisory.Forecast[0].Position.Latitude, 6);
        Assert.Equal(150.0, advisory.Forecast[0].Position.Longitude, 6);
        Assert.All(advisory.Forecast, p => Assert.True(p.Position.Latitude < 0));
        Assert.Equal(new DateTime(2025, 2, 15, 18, 0, 0, DateTimeKind.Utc), advisory.ForecastEnd);
        Assert.Equal(new DateTimeOffset(2025, 2, 10, 22, 0, 0, TimeSpan.Zero), advisory.IssueTime);

        Assert.Equal((90.0, 120.0, 100.0, 80.0), Quadrants(advisory.Forecast[0].Radii[0]));

        var cone = Assert.IsType<TropicalCycloneCone>(advisory.Cone);
        Assert.True(Contains(cone.DaysOneToThree, advisory.Forecast.Single(p => p.Tau == 48).Position));
        Assert.True(Contains(cone.DaysFourToFive, advisory.Forecast.Single(p => p.Tau == 120).Position));
        Assert.False(Contains(cone.DaysOneToThree, advisory.Forecast.Single(p => p.Tau == 120).Position));
        Assert.All(cone.DaysFourToFive.SelectMany(s => s.ExteriorRing), p => Assert.True(p.Latitude < -17));

        // Past track up to the synoptic time; the 11 Feb point is after it.
        Assert.Equal(3, advisory.PastTrack.Count);
        Assert.Equal((70.0, 90.0, 80.0, 60.0), Quadrants(Assert.Single(advisory.PastTrack[1].Radii)));
        Assert.Empty(advisory.PastTrack[0].Radii);

        var extent = advisory.Extent!;
        Assert.True(extent.NorthLatitude < 0);
        Assert.Equal(-26.5, extent.SouthLatitude, 6);
    }

    [Fact]
    public void A_cone_across_the_antimeridian_is_split_in_one_piece()
    {
        var forecast = SyntheticNhc.ForecastZip("CP022025", "012", "1100 AM HST Wed Aug 06 2025", "Iona",
        [
            new(0, "06/1800", 15.0, -176.0, 100, 120, "MH", "M", 960),
            new(48, "08/1800", 16.0, -179.5, 90, 110, "HU", "H"),
            new(72, "09/1800", 16.5, 179.0, 80, 100, "HU", "H"),
            new(120, "11/1800", 17.5, 176.0, 65, 80, "HU", "H"),
        ], [(17.5, -176.5), (16.0, -174.5), (14.0, 179.0), (15.0, 174.0), (20.0, 174.0), (17.5, -176.5)]);

        var advisory = NhcAdvisoryReader.Read(new MemoryStream(forecast), null, null);

        var cone = Assert.IsType<TropicalCycloneCone>(advisory.Cone);
        Assert.NotEmpty(cone.DaysOneToThree);
        Assert.NotEmpty(cone.DaysFourToFive);
        Assert.Equal(new DateTimeOffset(2025, 8, 6, 11, 0, 0, TimeSpan.FromHours(-10)), advisory.IssueTime);
    }

    [Fact]
    public void Not_a_zip_is_a_format_error()
    {
        var path = Path.Combine(_dir, "al092022_5day_020.zip");
        File.WriteAllText(path, "not a zip");

        Assert.Throws<NhcFormatException>(() => NhcAdvisoryReader.Read(path));
    }

    [Fact]
    public void A_zip_with_no_forecast_points_is_a_format_error()
    {
        var ex = Assert.Throws<NhcFormatException>(() => NhcAdvisoryReader.Read(File.OpenRead(TropicalCycloneFixtures.IanWindRadii), null, null));
        Assert.Contains("_5day_pts", ex.Message);
    }

    [Fact]
    public void Looks_like_a_forecast_zip_from_its_first_entry()
    {
        Assert.True(NhcAdvisoryReader.LooksLikeForecastZip(Leading(TropicalCycloneFixtures.IanForecast)));
        Assert.False(NhcAdvisoryReader.LooksLikeForecastZip(Leading(TropicalCycloneFixtures.IanWindRadii)));
        Assert.False(NhcAdvisoryReader.LooksLikeForecastZip(Leading(TropicalCycloneFixtures.IanBestTrack)));
        Assert.False(NhcAdvisoryReader.LooksLikeForecastZip("GRIB"u8));

        var synthetic = SyntheticNhc.ForecastZip("AL142024", "030", "500 AM AST Sat Oct 12 2024", "Leslie",
            [new(0, "12/0600", 35.2, -30.1, 40, 50, "PTC", "D", 1004)], cone: null);
        Assert.True(NhcAdvisoryReader.LooksLikeForecastZip(synthetic.AsSpan(0, Math.Min(1024, synthetic.Length))));
    }

    [Theory]
    [InlineData("1100 PM EDT Tue Sep 27 2022", "2022-09-27T23:00:00-04:00")]
    [InlineData("500 AM HST Mon Aug 07 2023", "2023-08-07T05:00:00-10:00")]
    [InlineData("1200 PM CDT Fri Jun 02 2023", "2023-06-02T12:00:00-05:00")]
    [InlineData("1200 AM AST Sun Oct 01 2023", "2023-10-01T00:00:00-04:00")]
    [InlineData("11:00 AM EST Thu Nov 30 2023", "2023-11-30T11:00:00-05:00")]
    [InlineData("300 PM UTC Mon Feb 10 2025", "2025-02-10T15:00:00+00:00")]
    public void Reads_advisory_dates(string text, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), NhcAdvisoryReader.ParseAdvisoryDate(text));

    [Theory]
    [InlineData("1100 PM XYZ Tue Sep 27 2022")]
    [InlineData("Tuesday")]
    [InlineData("")]
    public void Unreadable_advisory_dates_are_null(string text) =>
        Assert.Null(NhcAdvisoryReader.ParseAdvisoryDate(text));

    [Fact]
    public void Valid_times_roll_into_the_next_month_and_year()
    {
        Assert.Equal(new DateTime(2022, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            NhcAdvisoryReader.ParseDayTime("01/0000", new DateTime(2022, 9, 30, 21, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            NhcAdvisoryReader.ParseDayTime("02/1200", new DateTime(2024, 12, 31, 18, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2022, 9, 30, 18, 0, 0, DateTimeKind.Utc),
            NhcAdvisoryReader.ParseDayTime("30/1800", new DateTime(2022, 10, 1, 3, 0, 0, DateTimeKind.Utc)));
        Assert.Null(NhcAdvisoryReader.ParseDayTime("2022092803", DateTime.UtcNow));
    }

    private static (double, double, double, double) Quadrants(TropicalCycloneWindRadii r) =>
        (r.NortheastNauticalMiles, r.SoutheastNauticalMiles, r.SouthwestNauticalMiles, r.NorthwestNauticalMiles);

    private static byte[] Leading(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes[..Math.Min(1024, bytes.Length)];
    }

    private static bool Contains(IReadOnlyList<Features.SurfacePart> surfaces, DataModel.GeoPosition point) =>
        surfaces.Any(s => InRing(s.ExteriorRing, point) && !s.InteriorRings.Any(h => InRing(h, point)));

    private static bool InRing(IReadOnlyList<DataModel.GeoPosition> ring, DataModel.GeoPosition p)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > p.Latitude) != (b.Latitude > p.Latitude)
                && p.Longitude < ((b.Longitude - a.Longitude) * (p.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Area(IReadOnlyList<Features.SurfacePart> surfaces) =>
        surfaces.Sum(s => Math.Abs(Shoelace(s.ExteriorRing)) - s.InteriorRings.Sum(h => Math.Abs(Shoelace(h))));

    private static double Shoelace(IReadOnlyList<DataModel.GeoPosition> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count - 1; i++)
            sum += (ring[i].Longitude * ring[i + 1].Latitude) - (ring[i + 1].Longitude * ring[i].Latitude);
        return sum / 2;
    }
}
