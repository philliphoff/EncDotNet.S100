namespace EncDotNet.S100.Datasets.NetCdf.Tests;

public sealed class CfTimeUnitsTests
{
    [Theory]
    [InlineData("hours since 2026-10-25 00:00:00", 6, "2026-10-25T06:00:00Z")]
    [InlineData("hours since 1950-01-01", 672, "1950-01-29T00:00:00Z")]
    [InlineData("seconds since 1970-01-01T00:00:00Z", 1792886400, "2026-10-25T00:00:00Z")]
    [InlineData("minutes since 2026-10-25 06:30", 30, "2026-10-25T07:00:00Z")]
    [InlineData("days since 2026-10-24", 1.25, "2026-10-25T06:00:00Z")]
    [InlineData("Hours Since 2026-10-25 0:0:0", 1, "2026-10-25T01:00:00Z")]
    [InlineData("hours since 2026-10-25 03:00:00 +03:00", 0, "2026-10-25T00:00:00Z")]
    [InlineData("hours since 2026-10-25 00:00:00.0 UTC", 3, "2026-10-25T03:00:00Z")]
    [InlineData("s since 2026-10-25T00:00:00-0100", 0, "2026-10-25T01:00:00Z")]
    public void Converts_values_to_UTC(string units, double value, string expected)
    {
        var parsed = CfTimeUnits.Parse(units);

        var time = parsed.ToUtc(value);

        Assert.Equal(DateTimeKind.Utc, time.Kind);
        Assert.Equal(DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal), time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("standard")]
    [InlineData("gregorian")]
    [InlineData("Gregorian")]
    [InlineData("proleptic_gregorian")]
    public void Supported_calendars_parse(string? calendar)
    {
        Assert.Equal(TimeSpan.FromHours(1), CfTimeUnits.Parse("hours since 2026-10-25", calendar).Unit);
    }

    [Theory]
    [InlineData("noleap")]
    [InlineData("360_day")]
    [InlineData("julian")]
    public void Other_calendars_fail_naming_the_calendar(string calendar)
    {
        var error = Assert.Throws<CfConventionsException>(() => CfTimeUnits.Parse("hours since 2026-10-25", calendar));

        Assert.Contains($"'{calendar}' calendar", error.Message);
        Assert.Contains("standard, gregorian and proleptic_gregorian", error.Message);
    }

    [Theory]
    [InlineData("months since 2026-01-01", "length varies")]
    [InlineData("fortnights since 2026-01-01", "'fortnights'")]
    [InlineData("hours", "aren't CF time units")]
    [InlineData("hours since yesterday", "can't be read")]
    public void Bad_units_fail_clearly(string units, string expected)
    {
        Assert.Contains(expected, Assert.Throws<CfConventionsException>(() => CfTimeUnits.Parse(units)).Message);
    }

    [Theory]
    [InlineData("2026-10-25T06:00:00Z", "2026-10-25T06:00:00Z")]
    [InlineData("2026-10-25", "2026-10-25T00:00:00Z")]
    [InlineData("2026-10-25 06:00:00 UTC", "2026-10-25T06:00:00Z")]
    public void Parses_ISO_times(string text, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal), CfTimeUnits.ParseTime(text));
    }

    [Fact]
    public void Unreadable_times_are_null()
    {
        Assert.Null(CfTimeUnits.ParseTime("soon"));
        Assert.Null(CfTimeUnits.ParseTime("2026-13-40"));
    }
}
