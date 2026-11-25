namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// The words <see cref="RuleConditionText"/> puts conditions into. They
/// moved here from the viewer's resources with the reader, so the CLI's
/// HTML export (R14, #1723) says cases as the Reference tab does.
/// </summary>
internal static class Strings
{
    // A listed value that names a thing, such as 'a withy'
    public const string ChartReference_Condition_ArticleA = "a {0}";

    public const string ChartReference_Condition_ArticleAn = "an {0}";

    public const string ChartReference_Condition_AtLeast = "{0} is at least {1}";

    public const string ChartReference_Condition_AtMost = "{0} is at most {1}";

    // {0} is an attribute, {1} one or more of its values
    public const string ChartReference_Condition_Excludes = "{0} does not include {1}";

    // A rule condition that can't be put into words: a test of values the rule works out itself
    public const string ChartReference_Condition_Expression = "In some cases";

    public const string ChartReference_Condition_ExpressionFails = "In other cases";

    public const string ChartReference_Condition_FeatureType = "Feature type";

    public const string ChartReference_Condition_Geometry = "Geometry";

    public const string ChartReference_Condition_Given = "{0} is given";

    public const string ChartReference_Condition_GreaterThan = "{0} is more than {1}";

    // {0} is an attribute, {1} one or more of its values: Restriction includes anchoring prohibited
    public const string ChartReference_Condition_Includes = "{0} includes {1}";

    // A rule condition: '{0}' is an attribute or setting name, '{1}' its value
    public const string ChartReference_Condition_Is = "{0} is {1}";

    public const string ChartReference_Condition_IsNot = "{0} is not {1}";

    public const string ChartReference_Condition_LessThan = "{0} is less than {1}";

    // A yes/no attribute that is false: Not radar conspicuous
    public const string ChartReference_Condition_No = "Not {0}";

    public const string ChartReference_Condition_NotGiven = "{0} is not given";

    public const string ChartReference_Condition_Off = "{0} is off";

    // A display setting condition, such as 'Simplified symbols is on'
    public const string ChartReference_Condition_On = "{0} is on";

    // The last two listed values joined, as in 'stake, pole, perch or post'
    public const string ChartReference_Condition_OrJoin = "{0} or {1}";

    // {0} is a list of values, {1} how many more there are
    public const string ChartReference_Condition_OrMore = "{0} or {1} more";

    public const string ChartReference_Condition_Otherwise = "None of the cases above";

    // A yes/no attribute that is true, by its name: Radar conspicuous
    public const string ChartReference_Condition_Yes = "{0}";

    public const string ChartReference_Geometry_Curve = "line";

    public const string ChartReference_Geometry_MultiPoint = "point set";

    public const string ChartReference_Geometry_Point = "point";

    public const string ChartReference_Geometry_Surface = "area";

    // A display setting compared with, when its value is unknown: the safety contour
    public const string Pick_Why_SettingFormat = "the {0}";

    // A display setting compared with, with its value: the safety contour (30 m)
    public const string Pick_Why_SettingValueFormat = "the {0} ({1})";

    // An attribute or setting with its value inline: Surrounding depth (10 m)
    public const string Pick_Why_ValueFormat = "{0} ({1})";
}
