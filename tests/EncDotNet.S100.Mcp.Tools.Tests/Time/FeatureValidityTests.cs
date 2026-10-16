using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Tests.Time;

public class FeatureValidityTests
{
    // S-124 Ed 2.0.0 nests the date (<dateStart><S100:date>…) and adds a UTC
    // time of day; the reader keeps the nested date as "dateStart.date".
    private static S124Feature Part(Dictionary<string, string> range) => new()
    {
        Id = "part",
        FeatureType = "NavwarnPart",
        GeometryType = S100GeometryType.Point,
        Points = [new GeoPosition(-41, -135)],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [new S124ComplexAttribute { Code = "fixedDateRange", SubAttributes = range }],
        References = [],
    };

    private static readonly S124Feature Edition2 = Part(new()
    {
        ["dateStart.date"] = "2026-03-01",
        ["timeOfDayStart"] = "06:00:00Z",
        ["dateEnd.date"] = "2026-03-01",
        ["timeOfDayEnd"] = "09:00:00Z",
    });

    [Theory]
    [InlineData(5, 59, FeatureValidity.Verdict.Disjoint)]
    [InlineData(6, 0, FeatureValidity.Verdict.Overlaps)]
    [InlineData(9, 0, FeatureValidity.Verdict.Overlaps)]
    [InlineData(9, 1, FeatureValidity.Verdict.Disjoint)]
    public void A_date_range_with_times_of_day_is_valid_between_them(int hour, int minute, FeatureValidity.Verdict expected)
    {
        var at = new DateTimeOffset(2026, 3, 1, hour, minute, 0, TimeSpan.Zero);

        Assert.Equal(expected, FeatureValidity.Check(Edition2, new TimeQuery.Instant(at)));
    }

    [Fact]
    public void A_plain_date_starts_at_midnight()
    {
        var part = Part(new() { ["dateStart"] = "2026-03-02" });

        Assert.Equal(FeatureValidity.Verdict.Disjoint, FeatureValidity.Check(part, new TimeQuery.Instant(new DateTimeOffset(2026, 3, 1, 23, 59, 0, TimeSpan.Zero))));
        Assert.Equal(FeatureValidity.Verdict.Overlaps, FeatureValidity.Check(part, new TimeQuery.Instant(new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero))));
    }
}
