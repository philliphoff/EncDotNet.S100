using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pick report values as S-98 15.1 asks (#1158): numbers without padding and
/// followed by their unit, dates as DD-MMM-YYYY.
/// </summary>
public sealed class PickValueFormatterTests
{
    private static SimpleAttribute Attribute(string valueType, string? unit = null) => new()
    {
        Name = "Test",
        Code = "test",
        ValueType = valueType,
        Uom = unit is null ? null : new UnitOfMeasure(unit, unit),
    };

    [Theory]
    [InlineData("real", "s", "5", "5 s")]
    [InlineData("real", "s", "2.50", "2.5 s")]
    [InlineData("real", "°", "010", "10°")]
    [InlineData("real", "°", "010.5", "10.5°")]
    [InlineData("real", "m", "-1.2", "-1.2 m")]
    [InlineData("integer", null, "007", "7")]
    [InlineData("integer", "kt", "12", "12 kt")]
    public void Numbers_lose_their_padding_and_take_their_unit(string type, string? unit, string value, string expected) =>
        Assert.Equal(expected, PickValueFormatter.Format(Attribute(type, unit), value));

    [Theory]
    [InlineData("real", "n/a")]
    [InlineData("integer", "1.5")]
    [InlineData("text", "010")]
    [InlineData("enumeration", "3")]
    [InlineData("boolean", "1")]
    public void Other_values_read_as_encoded(string type, string value) =>
        Assert.Null(PickValueFormatter.Format(Attribute(type, "m"), value));

    [Theory]
    [InlineData("20120816", "16-AUG-2012")]
    [InlineData("2012-08-16", "16-AUG-2012")]
    [InlineData("201208--", "AUG-2012")]
    [InlineData("1996----", "1996")]
    [InlineData("----03--", "MAR")]
    [InlineData("----0316", "16-MAR")]
    [InlineData("------05", "05")]
    public void Dates_read_day_month_year(string value, string expected)
    {
        Assert.Equal(expected, PickValueFormatter.FormatDate(value));
        Assert.Equal(expected, PickValueFormatter.Format(Attribute("S100_TruncatedDate"), value));
    }

    [Theory]
    [InlineData("--------")]
    [InlineData("20121316")]
    [InlineData("2012081")]
    [InlineData("2012AB16")]
    public void Malformed_dates_read_as_encoded(string value) =>
        Assert.Null(PickValueFormatter.FormatDate(value));
}
