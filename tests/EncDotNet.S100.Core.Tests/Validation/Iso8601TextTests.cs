using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Core.Tests.Validation;

public class Iso8601TextTests
{
    [Theory]
    [InlineData("20260902")]
    [InlineData("2026-09-02")]
    [InlineData(" 20240229 ")]
    public void IsCalendarDate_Accepts_Basic_And_Extended(string text)
        => Assert.True(Iso8601Text.IsCalendarDate(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20230229")]   // not a leap year
    [InlineData("2026-9-2")]
    [InlineData("202609")]
    [InlineData("2026/09/02")]
    [InlineData("2026-09-02T10:00:00Z")]
    public void IsCalendarDate_Rejects_Other_Text(string? text)
        => Assert.False(Iso8601Text.IsCalendarDate(text));

    [Theory]
    [InlineData("105406")]
    [InlineData("105406Z")]
    [InlineData("105406+0000")]
    [InlineData("105406-05")]
    [InlineData("105406.5Z")]
    [InlineData("1054")]
    [InlineData("10")]
    [InlineData("10:54:06")]
    [InlineData("10:54")]
    [InlineData("19:45:48.437304")]
    [InlineData("10:54:06+05:30")]
    [InlineData("10:54:06,5Z")]
    [InlineData("24:00:00")]
    [InlineData("23:59:60Z")]
    public void IsTimeOfDay_Accepts_Basic_And_Extended(string text)
        => Assert.True(Iso8601Text.IsTimeOfDay(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("25:00:00")]
    [InlineData("24:00:01")]
    [InlineData("10:60")]
    [InlineData("106006")]
    [InlineData("10:5406")]
    [InlineData("1054:06")]
    [InlineData("10:54:06+2400")]
    [InlineData("10:54:06Q")]
    [InlineData("1")]
    public void IsTimeOfDay_Rejects_Other_Text(string? text)
        => Assert.False(Iso8601Text.IsTimeOfDay(text));

    [Theory]
    [InlineData("20260902")]
    [InlineData("2026-09-02")]
    [InlineData("20260902T105406Z")]
    [InlineData("20260902T105406+0000")]
    [InlineData("2026-09-02T10:54:06Z")]
    [InlineData("2026-09-02T10:54:06.123+02:00")]
    [InlineData("20260902T10:54:06Z")]   // mixed, as some producers write it
    public void IsDateOrDateTime_Accepts_Dates_And_Date_Times(string text)
        => Assert.True(Iso8601Text.IsDateOrDateTime(text));

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("20260902T")]
    [InlineData("20260902T2500")]
    [InlineData("2026-09-02 10:54:06")]
    [InlineData("T105406")]
    public void IsDateOrDateTime_Rejects_Other_Text(string text)
        => Assert.False(Iso8601Text.IsDateOrDateTime(text));

    [Theory]
    [InlineData("20260902", 2026, 9, 2)]
    [InlineData("2026-09-02", 2026, 9, 2)]
    [InlineData(" 20240229 ", 2024, 2, 29)]
    public void TryParseCalendarDate_Parses_Basic_And_Extended(string text, int year, int month, int day)
    {
        Assert.True(Iso8601Text.TryParseCalendarDate(text, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("20230229")]
    [InlineData("2026-9-2")]
    [InlineData("")]
    public void TryParseCalendarDate_Rejects_Other_Text(string text)
        => Assert.False(Iso8601Text.TryParseCalendarDate(text, out _));

    [Theory]
    [InlineData("105406", "10:54:06", null)]
    [InlineData("105406Z", "10:54:06", "00:00")]
    [InlineData("105406z", "10:54:06", "00:00")]
    [InlineData("105406+0000", "10:54:06", "00:00")]
    [InlineData("105406-05", "10:54:06", "-05:00")]
    [InlineData("105406+0530", "10:54:06", "05:30")]
    [InlineData("10:54:06-05:00", "10:54:06", "-05:00")]
    [InlineData("1054", "10:54:00", null)]
    [InlineData("10", "10:00:00", null)]
    [InlineData("10:54", "10:54:00", null)]
    [InlineData("105406.5Z", "10:54:06.5", "00:00")]
    [InlineData("10:54:06,25", "10:54:06.25", null)]
    [InlineData("19:45:48.437304", "19:45:48.437304", null)]
    [InlineData("10:54:06.123456789", "10:54:06.1234567", null)]   // truncated to ticks
    [InlineData("24:00:00", "1.00:00:00", null)]
    [InlineData("23:59:60Z", "1.00:00:00", "00:00")]
    public void TryParseTimeOfDay_Parses_Clock_Reading_And_Zone(string text, string expectedTime, string? expectedOffset)
    {
        Assert.True(Iso8601Text.TryParseTimeOfDay(text, out var time, out var offset));
        Assert.Equal(TimeSpan.Parse(expectedTime, System.Globalization.CultureInfo.InvariantCulture), time);
        Assert.Equal(
            expectedOffset is null ? null : TimeSpan.Parse(expectedOffset.TrimStart('+'), System.Globalization.CultureInfo.InvariantCulture),
            offset);
    }

    [Theory]
    [InlineData("25:00:00")]
    [InlineData("24:00:00.5")]
    [InlineData("10:54:06+2400")]
    [InlineData("10:54:06+0060")]
    [InlineData("10:5406")]
    [InlineData("\u0661\u0660")]   // Arabic-Indic digits are not ISO 8601 digits
    public void TryParseTimeOfDay_Rejects_Other_Text(string text)
        => Assert.False(Iso8601Text.TryParseTimeOfDay(System.Text.RegularExpressions.Regex.Unescape(text), out _, out _));

    [Theory]
    [InlineData("20260902", "2026-09-02T00:00:00")]
    [InlineData("2026-09-02", "2026-09-02T00:00:00")]
    [InlineData("20260902T105406Z", "2026-09-02T10:54:06")]
    [InlineData("20260902T105406+0000", "2026-09-02T10:54:06")]
    [InlineData("20260902T105406", "2026-09-02T10:54:06")]          // no zone: taken as UTC
    [InlineData("2026-09-02T05:54:06-05:00", "2026-09-02T10:54:06")]
    [InlineData("2026-09-02T12:54:06+02", "2026-09-02T10:54:06")]
    [InlineData("2026-09-02T10:54:06.123+00:00", "2026-09-02T10:54:06.123")]
    [InlineData("20260902T10:54:06Z", "2026-09-02T10:54:06")]        // mixed
    [InlineData("2026-09-02T01:00:00+05:00", "2026-09-01T20:00:00")] // offset crosses midnight
    [InlineData("2026-09-02T24:00:00Z", "2026-09-03T00:00:00")]      // end of day
    public void TryParseDateOrDateTime_Returns_UTC(string text, string expectedUtc)
    {
        Assert.True(Iso8601Text.TryParseDateOrDateTime(text, out var utc));
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(
            DateTime.SpecifyKind(DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
            utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("20230229T000000Z")]
    [InlineData("20260902T")]
    [InlineData("2026-09-02 10:54:06")]
    [InlineData("9999-12-31T23:00:00-05:00")]   // valid text, but past DateTime.MaxValue in UTC
    public void TryParseDateOrDateTime_Rejects_Other_Text(string? text)
        => Assert.False(Iso8601Text.TryParseDateOrDateTime(text, out _));

    [Theory]
    [InlineData("20260902T105406+0000")]
    [InlineData("2026-09-02T10:54:06.5-05:00")]
    [InlineData("20260902T10:54:06Z")]
    [InlineData("20260902")]
    [InlineData("20230229")]
    [InlineData("20260902T2500")]
    [InlineData("nonsense")]
    public void IsDateOrDateTime_Agrees_With_TryParseDateOrDateTime(string text)
        => Assert.Equal(Iso8601Text.IsDateOrDateTime(text), Iso8601Text.TryParseDateOrDateTime(text, out _));
}
