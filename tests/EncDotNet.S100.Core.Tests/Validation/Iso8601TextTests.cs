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
}
