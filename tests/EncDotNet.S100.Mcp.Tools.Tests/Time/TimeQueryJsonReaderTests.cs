using EncDotNet.S100.Mcp.Tools.Time;

namespace EncDotNet.S100.Mcp.Tools.Tests.Time;

public class TimeQueryJsonReaderTests
{
    [Fact]
    public void Parses_instant_with_trailing_Z()
    {
        var q = TimeQueryJsonReader.Parse("""{"kind":"instant","t":"2024-01-01T14:00:00Z"}""");
        var i = Assert.IsType<TimeQuery.Instant>(q);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 14, 0, 0, TimeSpan.Zero), i.Value);
    }

    [Fact]
    public void Parses_instant_with_explicit_offset_normalising_to_utc()
    {
        var q = TimeQueryJsonReader.Parse("""{"kind":"instant","t":"2024-01-01T06:00:00-08:00"}""");
        var i = Assert.IsType<TimeQuery.Instant>(q);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 14, 0, 0, TimeSpan.Zero), i.Value);
        Assert.Equal(TimeSpan.Zero, i.Value.Offset);
    }

    [Fact]
    public void Parses_range()
    {
        var q = TimeQueryJsonReader.Parse(
            """{"kind":"range","from":"2024-01-01T00:00:00Z","to":"2024-01-01T06:00:00Z"}""");
        var r = Assert.IsType<TimeQuery.Range>(q);
        Assert.Equal(TimeSpan.FromHours(6), r.Duration);
    }

    [Fact]
    public void Parses_series_with_step_seconds()
    {
        var q = TimeQueryJsonReader.Parse(
            """{"kind":"series","from":"2024-01-01T00:00:00Z","to":"2024-01-01T06:00:00Z","stepSeconds":1800}""");
        var s = Assert.IsType<TimeQuery.Series>(q);
        Assert.Equal(TimeSpan.FromMinutes(30), s.Step);
        Assert.Equal(13, s.EstimatedCount); // 00, 0:30, 1:00, ..., 6:00
    }

    [Fact]
    public void Rejects_naive_timestamp_without_offset()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse("""{"kind":"instant","t":"2024-01-01T14:00:00"}"""));
    }

    [Fact]
    public void Rejects_unknown_kind()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse("""{"kind":"forever"}"""));
    }

    [Fact]
    public void Rejects_missing_kind()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse("""{"t":"2024-01-01T00:00:00Z"}"""));
    }

    [Fact]
    public void Rejects_reversed_range()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse(
                """{"kind":"range","from":"2024-01-01T06:00:00Z","to":"2024-01-01T00:00:00Z"}"""));
    }

    [Fact]
    public void Rejects_series_with_zero_step()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse(
                """{"kind":"series","from":"2024-01-01T00:00:00Z","to":"2024-01-01T06:00:00Z","stepSeconds":0}"""));
    }

    [Fact]
    public void Rejects_series_with_missing_step()
    {
        Assert.Throws<ArgumentException>(() =>
            TimeQueryJsonReader.Parse(
                """{"kind":"series","from":"2024-01-01T00:00:00Z","to":"2024-01-01T06:00:00Z"}"""));
    }

    [Fact]
    public void Rejects_non_object_payload()
    {
        Assert.Throws<ArgumentException>(() => TimeQueryJsonReader.Parse("\"hello\""));
        Assert.Throws<ArgumentException>(() => TimeQueryJsonReader.Parse("[1,2]"));
    }

    [Fact]
    public void Rejects_empty_string()
    {
        Assert.Throws<ArgumentException>(() => TimeQueryJsonReader.Parse(""));
    }
}
