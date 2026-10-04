using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Core.Tests;

/// <summary>The issueDate + issueTime parser (#720), over the forms producers write.</summary>
public sealed class S100IssueTimeTests
{
    [Theory]
    [InlineData("20210414", "135227Z", "2021-04-14T13:52:27")]
    [InlineData("2026-10-03", "13:45:43.635093", "2026-10-03T13:45:43.635093")]
    [InlineData("20251220", "031803Z", "2025-12-20T03:18:03")]
    [InlineData("2026-10-03", "13:45", "2026-10-03T13:45:00")]
    [InlineData("2026-10-03", "1345", "2026-10-03T13:45:00")]
    [InlineData("2026-10-03", "14:45:00+01:00", "2026-10-03T13:45:00")]
    [InlineData("2026-10-03", "084500-0500", "2026-10-03T13:45:00")]
    [InlineData("2026-10-03", "135227+0000", "2026-10-03T13:52:27")]
    [InlineData("2026-10-03", "08:45:00-05", "2026-10-03T13:45:00")]
    [InlineData("2026-10-03", "13:45:43.635093Z", "2026-10-03T13:45:43.635093")]
    [InlineData("2026-10-03", "01:00:00+05:00", "2026-10-02T20:00:00")]
    [InlineData("2026-10-03", "13", "2026-10-03T13:00:00")]
    [InlineData("2026-10-03", null, "2026-10-03T00:00:00")]
    [InlineData("2026-10-03", "", "2026-10-03T00:00:00")]
    [InlineData("2026-10-03", "25:00:00", "2026-10-03T00:00:00")]
    [InlineData("2026-10-03", "soon", "2026-10-03T00:00:00")]
    public void Parses_the_date_and_time_as_UTC(string date, string? time, string expected)
    {
        var parsed = S100IssueTime.Parse(date, time);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("03/10/2026")]
    [InlineData("20230229")]
    public void No_readable_date_is_no_time(string? date) => Assert.Null(S100IssueTime.Parse(date, "120000Z"));
}
