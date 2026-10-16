using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Core.Tests;

public class S100DateTimesTests
{
    [Theory]
    [InlineData("2026-03-01", null, "2026-03-01T00:00:00Z")]
    [InlineData("20260301", "", "2026-03-01T00:00:00Z")]
    [InlineData("2026-03-01", "06:30:00Z", "2026-03-01T06:30:00Z")]
    [InlineData("2026-03-01", "06:30:00", "2026-03-01T06:30:00Z")]
    [InlineData("2026-03-01", "063000Z", "2026-03-01T06:30:00Z")]
    [InlineData("2026-03-01", "06:30", "2026-03-01T06:30:00Z")]
    [InlineData("2026-03-01T06:30:00Z", null, "2026-03-01T06:30:00Z")]
    public void Combines_a_date_and_a_time_of_day_in_UTC(string date, string? time, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), S100DateTimes.Combine(date, time));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("--03-01", null)]
    [InlineData("2026-03-01", "noon")]
    [InlineData("yesterday", null)]
    public void Returns_null_for_what_isnt_a_moment(string? date, string? time)
    {
        Assert.Null(S100DateTimes.Combine(date, time));
    }
}
