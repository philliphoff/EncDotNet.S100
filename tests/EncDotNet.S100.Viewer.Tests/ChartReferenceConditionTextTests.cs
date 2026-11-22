using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Rule conditions in words (#1637, symbol dictionary design note §4.3,
/// §4.5), with the S-101 feature catalogue's labels. Chart reference's Drawn
/// with uses <see cref="RuleConditionText"/> now; the Reference tab and
/// Object information will reuse it.
/// </summary>
public sealed class ChartReferenceConditionTextTests
{
    [Fact]
    public async Task Lateral_Beacons_BCNLAT21_reads_as_the_design_note_says()
    {
        var index = await ChartReferenceIndexes.S101Async();
        var beacon = ChartReferenceIndexes.Feature(index, "LateralBeacon");

        // The case with one colour: colour[1] == 3 and beaconShape == 1.
        var use = beacon.Uses.First(u =>
            u.ItemId == "BCNLAT21"
            && u.Conditions.Count(c => c.Subject == "colour") == 1
            && u.Conditions.Any(c => c is { Subject: "beaconShape", Value: "1" }));

        Assert.Equal(
            "Simplified symbols is on · Colour is red · Beacon shape is a stake, pole, perch or post",
            RuleConditionText.Describe(use.Conditions, index, includeGeometry: false));
        Assert.StartsWith("Geometry is point · ", RuleConditionText.Describe(use.Conditions, index), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Consecutive_bands_of_one_attribute_read_as_one_list()
    {
        var index = await ChartReferenceIndexes.S101Async();

        var text = RuleConditionText.Describe(
            [Attr("colour", 3, 1), Attr("colour", 4, 2), Attr("colour", 3, 3), Attr("beaconShape", 2)],
            index);

        Assert.Equal("Colour is red, green, red · Beacon shape is a withy", text);
    }

    [Fact]
    public async Task Each_kind_of_condition_has_its_words()
    {
        var index = await ChartReferenceIndexes.S101Async();
        string Say(RuleCondition c) => RuleConditionText.Describe(c, index);

        Assert.Equal("Simplified symbols is off", Say(Setting("SimplifiedSymbols", ConditionOperator.IsFalse)));
        Assert.Equal("Safety contour is less than 10", Say(Setting("SafetyContour", ConditionOperator.LessThan, "10")));
        Assert.Equal("Colour is not white", Say(Attr("colour", 1) with { Operator = ConditionOperator.NotEqual }));
        Assert.Equal("Colour is given", Say(Attr("colour", 1) with { Operator = ConditionOperator.Present, Value = null }));
        Assert.Equal("Category of landmark is a tower", Say(Attr("categoryOfLandmark", 17)));
        Assert.Equal("Category of landmark is an obelisk", Say(Attr("categoryOfLandmark", 12)));
        Assert.Equal("Geometry is area", Say(new RuleCondition { Kind = RuleConditionKind.Primitive, Operator = ConditionOperator.Equal, Value = "Surface" }));
        Assert.Equal("Feature type is Dredged Area", Say(new RuleCondition { Kind = RuleConditionKind.FeatureType, Operator = ConditionOperator.Equal, Value = "DredgedArea" }));
        Assert.Equal("None of the cases above", Say(new RuleCondition { Kind = RuleConditionKind.Otherwise, Operator = ConditionOperator.Holds }));
        Assert.Equal("Rule test: a or b", Say(Expression("a or b", ConditionOperator.Holds)));
        Assert.Equal("Rule test fails: a or b", Say(Expression("a or b", ConditionOperator.DoesNotHold)));

        // A code the catalogue doesn't know is shown as it is.
        Assert.Equal("madeUp is 7", Say(Attr("madeUp", 7)));
        Assert.Equal(string.Empty, RuleConditionText.Describe([], index));
    }

    [Fact]
    public void Names_read_in_sentence_case_and_keep_acronyms()
    {
        Assert.Equal("Beacon shape", RuleConditionText.SentenceCase("Beacon Shape"));
        Assert.Equal("Fog signals, RADAR", RuleConditionText.SentenceCase("Fog Signals, RADAR"));
    }

    private static RuleCondition Attr(string code, int value, int? index = null) => new()
    {
        Kind = RuleConditionKind.Attribute,
        Operator = ConditionOperator.Equal,
        Subject = code,
        Index = index,
        Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static RuleCondition Setting(string id, ConditionOperator op, string? value = null) => new()
    {
        Kind = RuleConditionKind.ContextParameter,
        Operator = op,
        Subject = id,
        Value = value,
    };

    private static RuleCondition Expression(string text, ConditionOperator op) => new()
    {
        Kind = RuleConditionKind.Expression,
        Operator = op,
        Text = text,
    };
}
