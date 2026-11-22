namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// What a <see cref="RuleCondition"/> tests.
/// </summary>
public enum RuleConditionKind
{
    /// <summary>
    /// A feature attribute: <see cref="RuleCondition.Subject"/> is the
    /// attribute code, with an optional <see cref="RuleCondition.Index"/>
    /// and a <see cref="RuleCondition.Value"/> that is a listed value code
    /// or a number.
    /// </summary>
    Attribute,

    /// <summary>
    /// A portrayal context parameter (a display setting):
    /// <see cref="RuleCondition.Subject"/> is its id.
    /// </summary>
    ContextParameter,

    /// <summary>
    /// The feature's type, as a shared rule tests it
    /// (<c>feature.Code == 'DredgedArea'</c>): <see cref="RuleCondition.Value"/>
    /// is the feature type code.
    /// </summary>
    FeatureType,

    /// <summary>
    /// The feature's geometric primitive: <see cref="RuleCondition.Value"/>
    /// is <c>Point</c>, <c>Curve</c>, <c>Surface</c> and so on.
    /// </summary>
    Primitive,

    /// <summary>
    /// A test the reader can't break down, such as a computed value or an
    /// <c>or</c>. <see cref="RuleCondition.Text"/> holds the source text.
    /// </summary>
    Expression,

    /// <summary>
    /// An <c>else</c> or <c>xsl:otherwise</c> branch after several cases:
    /// none of the cases above held.
    /// </summary>
    Otherwise,
}
