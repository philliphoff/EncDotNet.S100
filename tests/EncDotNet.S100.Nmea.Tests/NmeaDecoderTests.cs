using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Nmea.Tests;

public sealed class NmeaDecoderTests
{
    private static T Decode<T>(string body) where T : NmeaMessage
    {
        Assert.True(NmeaDecoder.TryDecode(NmeaSentence.Format(body), out var message));
        return Assert.IsType<T>(message);
    }

    [Fact]
    public void Mwv_relative_wind_in_knots()
    {
        var mwv = Decode<MwvSentence>("IIMWV,083.0,R,13.8,N,A");

        Assert.Equal("II", mwv.Talker);
        Assert.Equal(WindReference.Relative, mwv.Reference);
        Assert.Equal(83.0, mwv.Angle!.Value.TotalDegrees);
        Assert.Equal(13.8, mwv.Speed!.Value.TotalKnots, 9);
        Assert.True(mwv.IsValid);
    }

    [Theory]
    [InlineData("N", 10.0, 10.0)]
    [InlineData("M", 5.0, 9.719)] // 5 m/s
    [InlineData("K", 18.52, 10.0)] // 18.52 km/h
    [InlineData("S", 11.5078, 10.0)] // 11.5078 statute mph
    public void Mwv_speed_units_convert_to_knots(string unit, double value, double knots)
    {
        var mwv = Decode<MwvSentence>($"WIMWV,045.0,T,{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{unit},A");

        Assert.Equal(WindReference.True, mwv.Reference);
        Assert.Equal(knots, mwv.Speed!.Value.TotalKnots, 2);
    }

    [Fact]
    public void Mwv_with_an_unknown_unit_has_no_speed_and_status_V_is_invalid()
    {
        Assert.Null(Decode<MwvSentence>("WIMWV,045.0,R,12.0,X,A").Speed);
        Assert.False(Decode<MwvSentence>("WIMWV,045.0,R,12.0,N,V").IsValid);
    }

    [Fact]
    public void Mwv_without_a_reference_is_not_decoded()
    {
        Assert.False(NmeaDecoder.TryDecode(NmeaSentence.Format("WIMWV,045.0,,12.0,N,A"), out _));
    }

    [Fact]
    public void The_talker_does_not_matter()
    {
        foreach (var talker in new[] { "II", "WI", "VW", "YX", "GN" })
            Assert.Equal(83.0, Decode<MwvSentence>($"{talker}MWV,083.0,R,13.8,N,A").Angle!.Value.TotalDegrees);
    }

    [Fact]
    public void Mwd_true_and_magnetic_direction_and_speed()
    {
        var mwd = Decode<MwdSentence>("IIMWD,200.0,T,214.5,M,14.0,N,7.2,M");

        Assert.Equal(200.0, mwd.TrueDirection!.Value.TotalDegrees);
        Assert.Equal(214.5, mwd.MagneticDirection!.Value.TotalDegrees);
        Assert.Equal(14.0, mwd.Speed!.Value.TotalKnots, 9);
    }

    [Fact]
    public void Mwd_falls_back_to_metres_per_second_and_allows_null_fields()
    {
        var mwd = Decode<MwdSentence>("WIMWD,,T,214.5,M,,N,7.2,M");

        Assert.Null(mwd.TrueDirection);
        Assert.Equal(214.5, mwd.MagneticDirection!.Value.TotalDegrees);
        Assert.Equal(7.2, mwd.Speed!.Value.TotalMetresPerSecond, 9);
    }

    [Fact]
    public void Mtw_water_temperature()
    {
        Assert.Equal(12.4, Decode<MtwSentence>("IIMTW,12.4,C").Celsius);
        Assert.Null(Decode<MtwSentence>("IIMTW,54.3,F").Celsius);
        Assert.Null(Decode<MtwSentence>("IIMTW,,C").Celsius);
    }

    [Fact]
    public void Xdr_air_temperature_and_pressure_in_bars()
    {
        var xdr = Decode<XdrSentence>("WIXDR,C,10.8,C,AIRTEMP,P,1.0132,B,BARO");

        Assert.Equal(2, xdr.Measurements.Count);
        Assert.Equal(new XdrMeasurement('C', 10.8, "C", "AIRTEMP"), xdr.Measurements[0]);
        Assert.Equal(10.8, xdr.AirTemperatureCelsius);
        Assert.Equal(1013.2, xdr.PressureHectopascals!.Value, 6);
    }

    [Theory]
    [InlineData("YXXDR,P,101320,P,Barometer", 1013.2)] // pascals
    [InlineData("YXXDR,P,1013.2,P,ENV_ATMOS_P", 1013.2)] // hectopascals mislabelled as pascals
    [InlineData("WIXDR,P,1.0132,B,", 1013.2)] // no name
    public void Xdr_pressure_in_other_forms(string body, double hpa)
    {
        Assert.Equal(hpa, Decode<XdrSentence>(body).PressureHectopascals!.Value, 6);
    }

    [Theory]
    [InlineData("IIXDR,C,78.5,C,ENGINE#0")] // an engine, and out of range anyway
    [InlineData("IIXDR,C,20.5,C,CABIN")]
    [InlineData("IIXDR,C,15.2,C,WATER")]
    [InlineData("IIXDR,C,59.0,F,AIRTEMP")] // Fahrenheit isn't read
    [InlineData("IIXDR,P,3.2,B,ENGINE#0_OIL")]
    [InlineData("IIXDR,P,2.1,B,")] // out of the barometric range
    [InlineData("IIXDR,H,65.0,P,HUMIDITY")]
    public void Xdr_readings_that_are_not_outside_air_are_ignored(string body)
    {
        var xdr = Decode<XdrSentence>(body);

        Assert.Null(xdr.AirTemperatureCelsius);
        Assert.Null(xdr.PressureHectopascals);
    }

    [Fact]
    public void Xdr_picks_the_weather_reading_among_others()
    {
        var xdr = Decode<XdrSentence>("IIXDR,C,82.0,C,ENGINE#0,C,9.5,C,TempAir,P,0.9981,B,Barometer,H,72.0,P,Humidity");

        Assert.Equal(4, xdr.Measurements.Count);
        Assert.Equal(9.5, xdr.AirTemperatureCelsius);
        Assert.Equal(998.1, xdr.PressureHectopascals!.Value, 6);
    }

    [Fact]
    public void Hdt_and_hdg_headings()
    {
        Assert.Equal(90.0, Decode<HdtSentence>("HEHDT,90.0,T").Heading!.Value.TotalDegrees);

        var hdg = Decode<HdgSentence>("HCHDG,104.5,1.0,W,14.5,W");
        Assert.Equal(104.5, hdg.MagneticSensorHeading!.Value.TotalDegrees);
        Assert.Equal(-1.0, hdg.Deviation!.Value.TotalDegrees);
        Assert.Equal(-14.5, hdg.Variation!.Value.TotalDegrees);
        Assert.Equal(89.0, hdg.TrueHeading!.Value.TotalDegrees, 9);

        // Wraps through north, and needs the variation.
        Assert.Equal(5.0, Decode<HdgSentence>("HCHDG,355.0,,,10.0,E").TrueHeading!.Value.TotalDegrees, 9);
        Assert.Null(Decode<HdgSentence>("HCHDG,355.0,,,,").TrueHeading);
    }

    [Fact]
    public void Vhw_heading_and_speed_through_water()
    {
        var vhw = Decode<VhwSentence>("IIVHW,90.0,T,104.5,M,6.0,N,11.1,K");
        Assert.Equal(90.0, vhw.TrueHeading!.Value.TotalDegrees);
        Assert.Equal(104.5, vhw.MagneticHeading!.Value.TotalDegrees);
        Assert.Equal(6.0, vhw.SpeedThroughWater!.Value.TotalKnots, 9);

        var kmh = Decode<VhwSentence>("VWVHW,,T,,M,,N,11.112,K");
        Assert.Null(kmh.TrueHeading);
        Assert.Equal(6.0, kmh.SpeedThroughWater!.Value.TotalKnots, 3);
    }

    [Fact]
    public void Rmc_position_time_motion_and_variation()
    {
        var rmc = Decode<RmcSentence>("GPRMC,145000.00,A,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,A");

        Assert.True(rmc.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 14, 50, 0, TimeSpan.Zero), rmc.Time);
        Assert.Equal(42.35, rmc.Position!.Value.Latitude, 9);
        Assert.Equal(-70.65, rmc.Position!.Value.Longitude, 9);
        Assert.Equal(6.5, rmc.SpeedOverGround!.Value.TotalKnots, 9);
        Assert.Equal(95.0, rmc.CourseOverGround!.Value.TotalDegrees);
        Assert.Equal(-14.5, rmc.MagneticVariation!.Value.TotalDegrees);
    }

    [Theory]
    [InlineData("GPRMC,145000.00,V,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,N")]
    [InlineData("GPRMC,145000.00,A,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,N")]
    public void An_invalid_rmc_fix_has_no_position_or_motion(string body)
    {
        var rmc = Decode<RmcSentence>(body);

        Assert.False(rmc.IsValid);
        Assert.Null(rmc.Position);
        Assert.Null(rmc.SpeedOverGround);
        Assert.Null(rmc.CourseOverGround);
    }

    [Fact]
    public void An_older_rmc_without_the_mode_field_and_southern_eastern_positions()
    {
        var rmc = Decode<RmcSentence>("GPRMC,235959,A,3351.500,S,15112.300,E,0.0,,010199,,");

        Assert.True(rmc.IsValid);
        Assert.Equal(new DateTimeOffset(1999, 1, 1, 23, 59, 59, TimeSpan.Zero), rmc.Time);
        Assert.Equal(-33.858333, rmc.Position!.Value.Latitude, 5);
        Assert.Equal(151.205, rmc.Position!.Value.Longitude, 5);
        Assert.Equal(0.0, rmc.SpeedOverGround!.Value.TotalKnots);
        Assert.Null(rmc.CourseOverGround);
        Assert.Null(rmc.MagneticVariation);
    }

    [Fact]
    public void Vtg_course_and_speed_over_ground()
    {
        var vtg = Decode<VtgSentence>("GPVTG,95.0,T,109.5,M,6.5,N,12.0,K,A");
        Assert.True(vtg.IsValid);
        Assert.Equal(95.0, vtg.CourseOverGround!.Value.TotalDegrees);
        Assert.Equal(6.5, vtg.SpeedOverGround!.Value.TotalKnots, 9);

        var invalid = Decode<VtgSentence>("GPVTG,95.0,T,109.5,M,6.5,N,12.0,K,N");
        Assert.False(invalid.IsValid);
        Assert.Null(invalid.SpeedOverGround);
    }

    [Theory]
    [InlineData("$GPGGA,145000.00,4221.000,N,07039.000,W,1,09,0.9,2.1,M,-29.0,M,,*53")]
    [InlineData("$PGRMZ,7,f,3*1C")]
    [InlineData("!AIVDM,1,1,,A,15MgK45P3@G?fl0E`JbR0OwT0@MS,0*4E")]
    public void Other_sentences_are_not_decoded(string line)
    {
        Assert.True(NmeaSentence.TryParse(line, out var sentence, out _));
        Assert.False(NmeaDecoder.TryDecode(sentence, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void Out_of_range_directions_are_dropped()
    {
        Assert.Null(Decode<HdtSentence>("HEHDT,361.0,T").Heading);
        Assert.Null(Decode<HdtSentence>("HEHDT,-1.0,T").Heading);
        Assert.Equal(0.0, Decode<HdtSentence>("HEHDT,360.0,T").Heading!.Value.TotalDegrees);
    }

    [Fact]
    public void ToSpeed_rejects_negative_values_and_unknown_units()
    {
        Assert.Null(NmeaDecoder.ToSpeed(-1, 'N'));
        Assert.Null(NmeaDecoder.ToSpeed(1, 'Z'));
        Assert.Null(NmeaDecoder.ToSpeed(null, 'N'));
        Assert.Equal(Speed.FromKnots(3), NmeaDecoder.ToSpeed(3, 'N'));
    }
}
