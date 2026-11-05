using System.Text;
using EncDotNet.S100.DataModel;
using static EncDotNet.S100.TestSupport.CodsusFixtures;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Issue #1295: the WPC coded surface bulletins — standard and high
/// resolution analyses and the coded forecast bulletins — read into the
/// surface analysis model, with wrapped lines, fronts crossing 180°, and the
/// rule for which side a front's pips go on.
/// </summary>
public sealed class CodedSurfaceBulletinReaderTests
{
    private static readonly DateTimeOffset Nov4 = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    // WPC's "Reading the High Resolution Coded Surface Bulletin" example (a US
    // Government work): no header, so it needs a reference time.
    private const string WpcHighResolutionExample = """
        VALID 120612Z
        HIGHS 1036 4391169 1037 4051079 1031 3850701 1033 4901183
        1026 3220892 1022 3571272
        LOWS 1018 4681013 1017 3420986 998 5040817 1002 5770777 999 7491590 998
        6820769 999 7611335 986 6300408 985 6910524 994 6410555 968 5410273
        STNRY 2660729 2530763 2350807 2230836 2060863 1820877 1580880
        TROF 3410986 3291003 3191023 3121044
        COLD 4440850 4290863 4160879 4030904 3980931 4050954 4180966
        OCFNT 5030817 4830826 4620838 4440850
        WARM 5641373 5481339 5251313 4981295 4921292 4711283 4531278
        TROF 3211165 3441198 3661223 3941243
        COLD 5631375 5161360 4651357 4181371 3941386
        **
        """;

    // WPC's "Reading the Coded Surface Bulletin" example: whole degrees, with intensities.
    private const string WpcStandardExample = """
        VALID 020603Z
        HIGHS 1029 34128 1036 4975 1036 4073 1034 41117 1035 44118
        1032 51117 1037 5893 1035 48106
        LOWS 971 59144 1010 3787 1011 3388 1011 3190 1006 24103
        1013 32104 1023 5094 994 5459
        OCFNT WK 60144 59137 55131 51129 49129
        COLD MDT 49129 44130 41131 38134 35136 33141
        STNRY STG 6135 6138 5942 5945 6048 6150
        OCFNT WK 5559 5555 5551 5348 5145 4944 4644
        WARM WK 3787 3786 3786 3686 3586 3486 3386
        COLD WK 3190 2892 2593 2294 2197 2098 21100
        STNRY WK 39105 40106 42108 45109 47111 50113 52114 55116 56117
        TROF 3887 3988 4090
        TROF 3887 3885 3983 3980 3979
        **
        """;

    // WPC's "Reading the Coded Short-Range Forecast Bulletin" example: a code
    // mid-line, and the "...." end of a partial bulletin.
    private const string WpcForecastExample = """
        CODED SURFACE FRONTAL POSITIONS FORECAST
        NWS WEATHER PREDICTION CENTER COLLEGE PARK MD
        314 AM EDT SAT AUG 19 2012

        SURFACE PROG VALID 201104081800Z
        HIGHS 1025 4040702 1013 5840947 1018 5091116 1020 2940837
        LOWS 1015 5230774 1011 3910826 998 3981047
        WARM 3910826 3840823 3770813 3650804 3590788 3580776 3510765
        3500750 3440740 3400731
        TROF 5230772 5100755 4850740
        COLD 5830871 5690888 5620910 5610935 5630953
        OCFNT 5450915 5300906 5050907 4850918 4670935 4560949 STNRY 4880910 4820932 4790950 4810970 4780984 4701002 4671022
        4581039 4561044
        TROF 3710978 3600982 3480992 3340997 3160995 3010998 2941004
        2891012
        ....
        """;

    [Fact]
    public void The_high_resolution_analysis_reads_its_header_valid_time_and_features()
    {
        var bulletin = CodedSurfaceBulletinReader.Read(Fixture(HighResolution));

        Assert.Equal(CodedSurfaceBulletinKind.Analysis, bulletin.Kind);
        Assert.Equal(CodedSurfaceBulletinResolution.HighResolution, bulletin.Resolution);
        Assert.Equal("ASUS02 KWBC 040000", bulletin.Header.WmoHeading);
        Assert.Equal("CODSUS", bulletin.Header.ProductId);
        Assert.Equal("CODED SURFACE FRONTAL POSITIONS", bulletin.Header.Title);
        Assert.Equal("NWS WEATHER PREDICTION CENTER COLLEGE PARK MD", bulletin.IssuingCentre);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 20, 34, 0, TimeSpan.FromHours(-5)), bulletin.Header.IssuedAt);
        Assert.Empty(bulletin.SkippedCodes);

        var analysis = Assert.Single(bulletin.Analyses);
        Assert.Equal(Nov4, analysis.ValidTime);
        Assert.Null(analysis.ForecastHours);
        Assert.Equal(bulletin.IssuingCentre, analysis.IssuingCentre);
        Assert.Equal(13, analysis.FrontsOf(SurfaceFrontType.Cold).Count());
        Assert.Equal(8, analysis.FrontsOf(SurfaceFrontType.Warm).Count());
        Assert.Equal(6, analysis.FrontsOf(SurfaceFrontType.Occluded).Count());
        Assert.Equal(18, analysis.FrontsOf(SurfaceFrontType.Stationary).Count());
        Assert.Equal(29, analysis.FrontsOf(SurfaceFrontType.Trough).Count());
        Assert.Equal(15, analysis.CentresOf(PressureCentreKind.High).Count());
        Assert.Equal(29, analysis.CentresOf(PressureCentreKind.Low).Count());
        Assert.All(analysis.Fronts, f => Assert.Equal(SurfaceFrontStrength.Unspecified, f.Strength));

        Assert.Equal(new PressureCentre(PressureCentreKind.High, 1026, new GeoPosition(50.1, -116.2)), analysis.PressureCentres[0]);
        var cold = analysis.Fronts[0];
        Assert.Equal(SurfaceFrontType.Cold, cold.Type);
        Assert.Equal("COLD", cold.Code);
        Assert.Equal(9, cold.Points.Count);
        Assert.Equal(new GeoPosition(41.7, -93.1), cold.Points[0]);
        Assert.Equal(new GeoPosition(27.4, -97.0), cold.Points[^1]);
    }

    [Fact]
    public void A_pressure_wrapped_away_from_its_position_stays_paired()
    {
        // "... 993 4521792 982" ends one line and "5341590 972 ..." starts the next.
        var analysis = CodedSurfaceBulletinReader.Read(Fixture(HighResolution)).Analyses[0];
        var lows = analysis.CentresOf(PressureCentreKind.Low).ToArray();

        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 993, new GeoPosition(45.2, -179.2)), lows);
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 982, new GeoPosition(53.4, -159.0)), lows);
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 972, new GeoPosition(56.7, -150.1)), lows);
    }

    [Fact]
    public void The_standard_analysis_of_the_same_time_reads_whole_degrees()
    {
        var bulletin = CodedSurfaceBulletinReader.Read(Fixture(Standard));

        Assert.Equal(CodedSurfaceBulletinResolution.Standard, bulletin.Resolution);
        Assert.Equal("ASUS01 KWBC 040134", bulletin.Header.WmoHeading);
        var analysis = Assert.Single(bulletin.Analyses);
        Assert.Equal(Nov4, analysis.ValidTime);
        Assert.Equal(new PressureCentre(PressureCentreKind.High, 1026, new GeoPosition(50, -116)), analysis.PressureCentres[0]);
        // Zero-padded latitudes and two-digit longitudes: 04162 is 4°N 162°W, 3707 is 37°N 7°W.
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 1011, new GeoPosition(4, -162)), analysis.PressureCentres);
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 1012, new GeoPosition(37, -7)), analysis.PressureCentres);
        Assert.Equal(13, analysis.FrontsOf(SurfaceFrontType.Cold).Count());
        Assert.Equal(15, analysis.CentresOf(PressureCentreKind.High).Count());
        Assert.Equal(29, analysis.CentresOf(PressureCentreKind.Low).Count());
    }

    [Fact]
    public void The_forecast_bulletin_has_one_analysis_per_forecast_time()
    {
        var bulletin = CodedSurfaceBulletinReader.Read(Fixture(Forecast));

        Assert.Equal(CodedSurfaceBulletinKind.Forecast, bulletin.Kind);
        Assert.Equal(CodedSurfaceBulletinResolution.Standard, bulletin.Resolution);
        Assert.Equal("CODSRP", bulletin.Header.ProductId);
        Assert.Equal("CODED SURFACE FRONTAL POSITIONS FORECAST", bulletin.Header.Title);
        Assert.Collection(
            bulletin.Analyses,
            twelve =>
            {
                Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), twelve.ValidTime);
                Assert.Equal(12, twelve.ForecastHours);
                Assert.Equal(7, twelve.FrontsOf(SurfaceFrontType.Cold).Count());
                Assert.Equal(3, twelve.FrontsOf(SurfaceFrontType.Warm).Count());
                Assert.Equal(7, twelve.CentresOf(PressureCentreKind.High).Count());
                Assert.Equal(8, twelve.CentresOf(PressureCentreKind.Low).Count());
            },
            day =>
            {
                Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), day.ValidTime);
                Assert.Equal(24, day.ForecastHours);
                Assert.Equal(5, day.CentresOf(PressureCentreKind.High).Count());
            });
    }

    [Fact]
    public void Every_front_type_and_both_kinds_of_centre_are_read_from_real_bulletins()
    {
        foreach (var name in new[] { HighResolution, Standard, Forecast })
        {
            var analysis = CodedSurfaceBulletinReader.Read(Fixture(name)).Analyses[0];
            Assert.All(Enum.GetValues<SurfaceFrontType>(), type => Assert.NotEmpty(analysis.FrontsOf(type)));
            Assert.All(Enum.GetValues<PressureCentreKind>(), kind => Assert.NotEmpty(analysis.CentresOf(kind)));
        }
    }

    [Fact]
    public void Intensities_wraps_mid_line_codes_and_fronts_across_180_degrees_are_read()
    {
        var bulletin = CodedSurfaceBulletinReader.Read(Fixture(Antimeridian));
        var analysis = Assert.Single(bulletin.Analyses);

        // Issued 1030 PM EST 31 Dec 2025 (03:30Z on 1 Jan 2026); VALID 123121Z is the year before.
        Assert.Equal(new DateTimeOffset(2025, 12, 31, 21, 0, 0, TimeSpan.Zero), analysis.ValidTime);
        Assert.Equal(["DRYLN"], bulletin.SkippedCodes);

        // 48185 is 185°W, so 175°E; its pressure ends the line before.
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 996, new GeoPosition(48, 175)), analysis.PressureCentres);
        Assert.Equal(2, analysis.CentresOf(PressureCentreKind.High).Count());
        Assert.Equal(3, analysis.CentresOf(PressureCentreKind.Low).Count());

        Assert.Collection(
            analysis.Fronts,
            warm => Assert.Equal((SurfaceFrontType.Warm, SurfaceFrontStrength.Strong), (warm.Type, warm.Strength)),
            cold =>
            {
                Assert.Equal((SurfaceFrontType.Cold, SurfaceFrontStrength.Moderate), (cold.Type, cold.Strength));
                Assert.Equal(new[] { -178.0, -180.0, 177.0, 174.0, 170.0 }, cold.Points.Select(p => p.Longitude));
                Assert.True(cold.CrossesAntimeridian);
            },
            trough =>
            {
                // TROF starts mid-line, after the cold front's last point.
                Assert.Equal((SurfaceFrontType.Trough, SurfaceFrontStrength.Unspecified), (trough.Type, trough.Strength));
                Assert.Equal([new GeoPosition(40, -165), new GeoPosition(38, -170)], trough.Points);
            },
            occluded => Assert.Equal((SurfaceFrontType.Occluded, SurfaceFrontStrength.Weak), (occluded.Type, occluded.Strength)),
            stationary =>
            {
                // Wholly east of 180°: 190°W to 200°W.
                Assert.Equal(new[] { 170.0, 165.0, 160.0 }, stationary.Points.Select(p => p.Longitude));
                Assert.False(stationary.CrossesAntimeridian);
            },
            wrapped => Assert.Equal([new GeoPosition(30, -150), new GeoPosition(31, -152)], wrapped.Points));
    }

    [Fact]
    public void Pips_go_on_the_left_of_the_point_order()
    {
        // The rule the reader documents: a cold front trailing from a low moves
        // east, a warm front leading from it moves north, and both are listed
        // from the low outwards, so the left of the point order faces the way
        // the front moves.
        var analysis = CodedSurfaceBulletinReader.Read(Fixture(HighResolution)).Analyses[0];
        var lows = analysis.CentresOf(PressureCentreKind.Low).Select(c => c.Position).ToArray();
        var fromLows = analysis.Fronts
            .Where(f => f.Points.Count > 1 && lows.Any(l => Math.Abs(l.Latitude - f.Points[0].Latitude) < 1 && Math.Abs(l.Longitude - f.Points[0].Longitude) < 1))
            .ToArray();

        var cold = fromLows.Where(f => f.Type == SurfaceFrontType.Cold).Select(LeftNormal).ToArray();
        var warm = fromLows.Where(f => f.Type == SurfaceFrontType.Warm).Select(LeftNormal).ToArray();
        Assert.True(cold.Length >= 3, $"{cold.Length} cold fronts start at a low");
        Assert.True(warm.Length >= 3, $"{warm.Length} warm fronts start at a low");
        // Analysts' fronts bend, so it's most, not all (66 of 68 cold and 26 of
        // 30 warm over the 15 analyses the reader's docs cite).
        Assert.True(cold.Count(n => n.East > 0) >= 0.8 * cold.Length, $"{cold.Count(n => n.East > 0)} of {cold.Length} cold fronts' left faces east");
        Assert.True(warm.Count(n => n.North > 0) >= 0.8 * warm.Length, $"{warm.Count(n => n.North > 0)} of {warm.Length} warm fronts' left faces north");
        // With the pips on the right, they'd face west and south.
        Assert.True(cold.Count(n => n.East < 0) <= 0.2 * cold.Length);
    }

    [Fact]
    public void WPCs_high_resolution_example_reads_with_a_reference_time()
    {
        var bulletin = CodedSurfaceBulletinReader.Parse(WpcHighResolutionExample, new DateTimeOffset(2011, 12, 6, 15, 0, 0, TimeSpan.Zero));

        var analysis = Assert.Single(bulletin.Analyses);
        Assert.Equal(new DateTimeOffset(2011, 12, 6, 12, 0, 0, TimeSpan.Zero), analysis.ValidTime);
        Assert.Equal(CodedSurfaceBulletinResolution.HighResolution, bulletin.Resolution);
        Assert.Null(bulletin.Header.WmoHeading);
        Assert.Equal(CodedSurfaceBulletinHeader.WpcName, bulletin.IssuingCentre);
        Assert.Equal(new PressureCentre(PressureCentreKind.High, 1036, new GeoPosition(43.9, -116.9)), analysis.PressureCentres[0]);
        Assert.Contains(new PressureCentre(PressureCentreKind.Low, 998, new GeoPosition(68.2, -76.9)), analysis.PressureCentres);
        Assert.Equal(6, analysis.CentresOf(PressureCentreKind.High).Count());
        Assert.Equal(11, analysis.CentresOf(PressureCentreKind.Low).Count());
        Assert.Equal(7, analysis.Fronts.Count);
    }

    [Fact]
    public void WPCs_standard_example_reads_its_intensities()
    {
        var analysis = CodedSurfaceBulletinReader.Parse(WpcStandardExample, new DateTimeOffset(2011, 2, 6, 9, 0, 0, TimeSpan.Zero)).Analyses[0];

        Assert.Equal(new DateTimeOffset(2011, 2, 6, 3, 0, 0, TimeSpan.Zero), analysis.ValidTime);
        Assert.Equal(
            [
                (SurfaceFrontType.Occluded, SurfaceFrontStrength.Weak),
                (SurfaceFrontType.Cold, SurfaceFrontStrength.Moderate),
                (SurfaceFrontType.Stationary, SurfaceFrontStrength.Strong),
                (SurfaceFrontType.Occluded, SurfaceFrontStrength.Weak),
                (SurfaceFrontType.Warm, SurfaceFrontStrength.Weak),
                (SurfaceFrontType.Cold, SurfaceFrontStrength.Weak),
                (SurfaceFrontType.Stationary, SurfaceFrontStrength.Weak),
                (SurfaceFrontType.Trough, SurfaceFrontStrength.Unspecified),
                (SurfaceFrontType.Trough, SurfaceFrontStrength.Unspecified),
            ],
            analysis.Fronts.Select(f => (f.Type, f.Strength)));
        Assert.Equal(new GeoPosition(21, -100), analysis.Fronts[5].Points[^1]);
    }

    [Fact]
    public void WPCs_forecast_example_reads_a_full_valid_time_and_a_code_mid_line()
    {
        var bulletin = CodedSurfaceBulletinReader.Parse(WpcForecastExample, referenceTime: null);

        Assert.Equal(CodedSurfaceBulletinKind.Forecast, bulletin.Kind);
        var analysis = Assert.Single(bulletin.Analyses);
        Assert.Equal(new DateTimeOffset(2011, 4, 8, 18, 0, 0, TimeSpan.Zero), analysis.ValidTime);
        Assert.Null(analysis.ForecastHours);
        var occluded = Assert.Single(analysis.FrontsOf(SurfaceFrontType.Occluded));
        var stationary = Assert.Single(analysis.FrontsOf(SurfaceFrontType.Stationary));
        Assert.Equal(6, occluded.Points.Count);
        Assert.Equal(9, stationary.Points.Count);
        Assert.Equal(new GeoPosition(45.6, -104.4), stationary.Points[^1]);
        Assert.Equal(8, analysis.FrontsOf(SurfaceFrontType.Trough).Single(t => t.Points.Count > 3).Points.Count);
    }

    [Fact]
    public void A_short_forecast_time_takes_the_month_of_the_issue_time_plus_its_lead()
    {
        const string text = """
            CODED SURFACE FRONTAL POSITIONS FORECAST
            NWS WEATHER PREDICTION CENTER COLLEGE PARK MD
            1000 PM EDT FRI OCT 30 2026

            36HR PROG VALID 011200Z
            HIGHS 1030 4070
            $$
            """;

        var analysis = CodedSurfaceBulletinReader.Parse(text, referenceTime: null).Analyses[0];

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), analysis.ValidTime);
        Assert.Equal(36, analysis.ForecastHours);
    }

    [Fact]
    public void A_bulletin_dated_only_by_its_WMO_heading_uses_the_reference_month()
    {
        const string text = "ASUS02 KWBC 040000\nCODSUS\n\nVALID 110400Z\nHIGHS 1026 5011162\n$$\n";

        var bulletin = CodedSurfaceBulletinReader.Parse(text, new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(Nov4, bulletin.Analyses[0].ValidTime);
        Assert.Null(bulletin.Header.IssuedAt);
    }

    [Fact]
    public void Read_from_a_stream_matches_read_from_a_file()
    {
        using var stream = File.OpenRead(Fixture(HighResolution));

        var fromStream = CodedSurfaceBulletinReader.Read(stream, referenceTime: null);
        var fromFile = CodedSurfaceBulletinReader.Read(Fixture(HighResolution));

        Assert.Equal(fromFile.Analyses[0].Fronts.Count, fromStream.Analyses[0].Fronts.Count);
        Assert.Equal(fromFile.Analyses[0].Fronts[^1].Points, fromStream.Analyses[0].Fronts[^1].Points);
    }

    [Fact]
    public void Carriage_returns_and_NOAAport_framing_are_ignored()
    {
        var raw = "\u0001\r\r\n824 \r\r\n" + File.ReadAllText(Fixture(Standard)).Replace("\n", "\r\r\n", StringComparison.Ordinal) + "\u0003";

        var analysis = CodedSurfaceBulletinReader.Parse(raw, referenceTime: null).Analyses[0];

        Assert.Equal(Nov4, analysis.ValidTime);
        Assert.Equal(29, analysis.CentresOf(PressureCentreKind.Low).Count());
    }

    [Theory]
    [InlineData("HIGHS 1026 5011162\n", "No VALID line")]
    [InlineData("VALID 110400Z\nHIGHS 1026 5011162 1022 44110\n", "standard (whole-degree) position in a bulletin of high-resolution")]
    [InlineData("VALID 110400Z\nHIGHS 1026 501116\n", "'501116' isn't a position")]
    [InlineData("VALID 110400Z\nHIGHS 1026 5011162 1022\n", "the HIGHS pressure 1022 has no position")]
    [InlineData("VALID 110400Z\nCOLD\nHIGHS 1026 5011162\n", "COLD has no points")]
    [InlineData("VALID 110400Z\n4170931 4060922\n", "isn't after a feature code")]
    [InlineData("VALID 110400Z\nCOLD 9500900 9400900\n", "off the globe")]
    [InlineData("VALID 1104Z\nCOLD 4170931 4060922\n", "isn't a valid time")]
    [InlineData("VALID 113400Z\nCOLD 4170931 4060922\n", "isn't a valid time")]
    public void A_bulletin_that_cant_be_decoded_says_what_and_where(string text, string expected)
    {
        var ex = Assert.Throws<CodedSurfaceBulletinFormatException>(() => CodedSurfaceBulletinReader.Parse(text, Nov4));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bulletin_with_no_issue_date_needs_a_reference_time()
    {
        var ex = Assert.Throws<CodedSurfaceBulletinFormatException>(() => CodedSurfaceBulletinReader.Parse(WpcHighResolutionExample, referenceTime: null));

        Assert.Contains("no issuance line", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Line 1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HighResolution)]
    [InlineData(Standard)]
    [InlineData(Forecast)]
    [InlineData(Antimeridian)]
    public void Every_fixture_looks_like_a_coded_surface_bulletin(string name)
    {
        var bytes = File.ReadAllBytes(Fixture(name));

        Assert.True(CodedSurfaceBulletinReader.LooksLikeCodedSurfaceBulletin(bytes.AsSpan(0, Math.Min(bytes.Length, 1024))));
    }

    [Theory]
    [InlineData(WpcHighResolutionExample)]
    [InlineData(WpcStandardExample)]
    [InlineData(WpcForecastExample)]
    [InlineData("ASUS02 KWBC 040000\nCODSUS\n\nVALID 110400Z\n")]
    public void WPCs_examples_and_a_bare_heading_look_like_bulletins(string text)
    {
        Assert.True(CodedSurfaceBulletinReader.LooksLikeCodedSurfaceBulletin(Encoding.ASCII.GetBytes(text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("VALID FROM 0400Z\nThe forecast is fair.\n")]
    [InlineData("<?xml version=\"1.0\"?><alert xmlns=\"urn:oasis:names:tc:emergency:cap:1.2\"/>")]
    [InlineData("{\"type\":\"FeatureCollection\",\"features\":[]}")]
    [InlineData("FXUS61 KBOX 040000\nAFDBOX\n\nArea Forecast Discussion\n")]
    public void Other_text_does_not(string text)
    {
        Assert.False(CodedSurfaceBulletinReader.LooksLikeCodedSurfaceBulletin(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void Binary_does_not()
    {
        byte[] bytes = [.. "CODED SURFACE FRONTAL POSITIONS"u8, 0x00, 0xFF];

        Assert.False(CodedSurfaceBulletinReader.LooksLikeCodedSurfaceBulletin(bytes));
    }

    [Fact]
    public void The_bulletin_records_WPCs_domain()
    {
        var bulletin = CodedSurfaceBulletinReader.Read(Fixture(HighResolution));

        Assert.Equal(CodedSurfaceBulletin.WpcDomain, bulletin.Domain);
        Assert.All(
            bulletin.Analyses[0].Fronts.SelectMany(f => f.Points),
            p => Assert.True(p.Latitude >= 0 && p.Longitude <= 0, $"{p} is in WPC's domain"));
    }

    // The unit vector to the left of a front's first segment, in local east/north.
    private static (double East, double North) LeftNormal(SurfaceFront front)
    {
        var (a, b) = (front.Points[0], front.Points[1]);
        double east = (b.Longitude - a.Longitude) * Math.Cos(a.Latitude * Math.PI / 180);
        double north = b.Latitude - a.Latitude;
        return (-north, east);
    }
}
