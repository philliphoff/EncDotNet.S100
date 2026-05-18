using System;
using System.Globalization;
using EncDotNet.S100.Viewer;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class TimeFormattingTests
{
    [Fact]
    public void Format_Utc_DateTime_Produces_IsoLike_String_With_UtcSuffix()
    {
        var dt = new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        var s = TimeFormatting.Format(dt, TimeFormat.Utc);
        Assert.Equal("2026-03-14 09:26:53 UTC", s);
    }

    [Fact]
    public void Format_Local_DateTime_Uses_CurrentCulture_ShortDateTime()
    {
        var dt = new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        var s = TimeFormatting.Format(dt, TimeFormat.Local);
        // 'g' produces short date + short time in CurrentCulture; assert no UTC suffix.
        Assert.DoesNotContain("UTC", s, StringComparison.Ordinal);
        var expected = dt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal(expected, s);
    }

    [Fact]
    public void Format_Unspecified_Kind_IsTreatedAs_Utc()
    {
        var unspec = new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        Assert.Equal(TimeFormatting.Format(utc, TimeFormat.Utc),
            TimeFormatting.Format(unspec, TimeFormat.Utc));
    }

    [Fact]
    public void Format_DateTimeOffset_Utc_RoundTrips()
    {
        var dto = new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.FromHours(5));
        var s = TimeFormatting.Format(dto, TimeFormat.Utc);
        Assert.Equal("2026-03-14 04:26:53 UTC", s);
    }

    [Fact]
    public void FormatDateOnly_Ignores_TimeFormat_Toggle()
    {
        var d = new DateOnly(2026, 3, 14);
        var s = TimeFormatting.FormatDateOnly(d);
        Assert.DoesNotContain("UTC", s, StringComparison.Ordinal);
        Assert.Equal(d.ToString("d", CultureInfo.CurrentCulture), s);
    }

    [Fact]
    public void FormatTimeRange_SameDay_Utc_OmitsSecondDate()
    {
        var a = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);
        var b = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);
        var s = TimeFormatting.FormatTimeRange(a, b, TimeFormat.Utc);
        Assert.Contains("2026-03-14", s);
        // Only one date occurrence.
        Assert.Equal(1, s.Split("2026-03-14").Length - 1);
        Assert.Contains("UTC", s);
    }

    [Fact]
    public void FormatTimeRange_DifferentDays_Utc_IncludesBothDates()
    {
        var a = new DateTime(2026, 3, 14, 23, 0, 0, DateTimeKind.Utc);
        var b = new DateTime(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc);
        var s = TimeFormatting.FormatTimeRange(a, b, TimeFormat.Utc);
        Assert.Contains("2026-03-14", s);
        Assert.Contains("2026-03-15", s);
    }
}
