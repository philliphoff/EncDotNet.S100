namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// One condition on the way to a portrayal instruction in a rule file,
/// kept as data so a viewer can put it into words with the feature
/// catalogue's labels, and match it against a picked feature.
/// </summary>
/// <remarks>
/// A <see cref="RuleUse"/> carries a list of conditions that must all hold.
/// For example, <c>feature.colour[1] == 3</c> in S-101's
/// <c>LateralBeacon.lua</c> reads as an
/// <see cref="RuleConditionKind.Attribute"/> condition with
/// <see cref="Subject"/> <c>colour</c>, <see cref="Index"/> 1,
/// <see cref="ConditionOperator.Equal"/> and <see cref="Value"/> <c>3</c>,
/// the listed value code for red.
/// </remarks>
public sealed record RuleCondition
{
    /// <summary>What the condition tests.</summary>
    public required RuleConditionKind Kind { get; init; }

    /// <summary>The test applied.</summary>
    public required ConditionOperator Operator { get; init; }

    /// <summary>
    /// The attribute code or context parameter id, or <see langword="null"/>
    /// for <see cref="RuleConditionKind.Primitive"/>,
    /// <see cref="RuleConditionKind.Expression"/> and
    /// <see cref="RuleConditionKind.Otherwise"/>.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The 1-based index of a multi-valued attribute, as in
    /// <c>colour[1]</c>, or <see langword="null"/> when none is given.
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// The value compared with: a listed value code, a number, or a
    /// primitive name. <see langword="null"/> for tests that take no value.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// The source text of an <see cref="RuleConditionKind.Expression"/>
    /// condition; <see langword="null"/> otherwise.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The 1-based line of the rule file the condition is read from: the
    /// <c>if</c>, <c>elseif</c> or <c>else</c> line in Lua, or the line of
    /// the template or test in XSLT.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// Returns the condition that holds when this one doesn't, as for the
    /// <c>else</c> of a lone <c>if</c>.
    /// </summary>
    /// <returns>The negated condition, at the same line.</returns>
    public RuleCondition Negate() => this with
    {
        Operator = Operator switch
        {
            ConditionOperator.Equal => ConditionOperator.NotEqual,
            ConditionOperator.NotEqual => ConditionOperator.Equal,
            ConditionOperator.LessThan => ConditionOperator.GreaterThanOrEqual,
            ConditionOperator.LessThanOrEqual => ConditionOperator.GreaterThan,
            ConditionOperator.GreaterThan => ConditionOperator.LessThanOrEqual,
            ConditionOperator.GreaterThanOrEqual => ConditionOperator.LessThan,
            ConditionOperator.Present => ConditionOperator.Absent,
            ConditionOperator.Absent => ConditionOperator.Present,
            ConditionOperator.IsTrue => ConditionOperator.IsFalse,
            ConditionOperator.IsFalse => ConditionOperator.IsTrue,
            ConditionOperator.Holds => ConditionOperator.DoesNotHold,
            _ => ConditionOperator.Holds,
        },
    };
}
