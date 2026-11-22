namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Where a feature type's rule names a portrayal item, colour fill, pen or
/// viewing group, and the conditions that lead there.
/// </summary>
/// <remarks>
/// The rules are read statically, so the conditions are those of the
/// enclosing <c>if</c>/<c>elseif</c>/<c>else</c> blocks (or XSLT template
/// match and tests). They can't account for computed values, and an
/// <c>else</c> after several cases reads as
/// <see cref="RuleConditionKind.Otherwise"/>. See
/// <c>docs/design/symbol-dictionary.md</c> §2.
/// </remarks>
public abstract class RuleUse
{
    /// <summary>
    /// The conditions that must all hold, outermost first. Empty when the
    /// instruction is unconditional.
    /// </summary>
    public IReadOnlyList<RuleCondition> Conditions { get; init; } = [];

    /// <summary>
    /// The rule file, without its extension, whose text names the
    /// instruction, such as <c>SEABED01</c>.
    /// </summary>
    public required string RuleFile { get; init; }

    /// <summary>The 1-based line in <see cref="RuleFile"/>.</summary>
    public int Line { get; init; }

    /// <summary>
    /// The shared rule the feature type's own rule requires to reach
    /// <see cref="RuleFile"/>, such as <c>DEPARE03</c> for Depth Area, or
    /// the XSLT named template its template calls, or
    /// <see langword="null"/> when the feature type's own rule names it.
    /// </summary>
    public string? ViaRule { get; init; }

    /// <summary>
    /// The chain of shared rules from the feature type's rule to
    /// <see cref="RuleFile"/>, such as <c>DEPARE03</c>, <c>SEABED01</c>.
    /// Empty when the feature type's own rule names the instruction.
    /// </summary>
    public IReadOnlyList<string> RulePath { get; init; } = [];
}
