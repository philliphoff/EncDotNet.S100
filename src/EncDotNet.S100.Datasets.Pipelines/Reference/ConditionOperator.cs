namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// The test a <see cref="RuleCondition"/> applies to its subject.
/// </summary>
public enum ConditionOperator
{
    /// <summary>The subject equals the value.</summary>
    Equal,

    /// <summary>The subject doesn't equal the value.</summary>
    NotEqual,

    /// <summary>The subject is less than the value.</summary>
    LessThan,

    /// <summary>The subject is less than or equal to the value.</summary>
    LessThanOrEqual,

    /// <summary>The subject is greater than the value.</summary>
    GreaterThan,

    /// <summary>The subject is greater than or equal to the value.</summary>
    GreaterThanOrEqual,

    /// <summary>The attribute has a value.</summary>
    Present,

    /// <summary>The attribute has no value.</summary>
    Absent,

    /// <summary>The context parameter is on.</summary>
    IsTrue,

    /// <summary>The context parameter is off.</summary>
    IsFalse,

    /// <summary>The expression holds. Used with <see cref="RuleConditionKind.Expression"/> and <see cref="RuleConditionKind.Otherwise"/>.</summary>
    Holds,

    /// <summary>The expression doesn't hold, as in the <c>else</c> of a lone <c>if</c>.</summary>
    DoesNotHold,
}
