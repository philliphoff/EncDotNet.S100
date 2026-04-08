namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Distinguishes between XSLT-based and Lua-based portrayal rules.
/// </summary>
public enum PortrayalRuleType
{
    Xslt,
    Lua,
}

/// <summary>
/// Metadata about a portrayal rule from the portrayal catalogue.
/// Used by the pipeline to determine which rules to execute for a given dataset.
/// </summary>
public sealed class PortrayalRule
{
    /// <summary>Rule file name (e.g. "DepthContour.xsl" or "LIGHTS05.lua").</summary>
    public required string Name { get; init; }

    /// <summary>Whether this rule is XSLT or Lua.</summary>
    public required PortrayalRuleType Type { get; init; }

    /// <summary>
    /// Execution order within the catalogue. Rules run in ascending order;
    /// later rules may reference symbols established by earlier ones.
    /// </summary>
    public required int ExecutionOrder { get; init; }

    /// <summary>
    /// Feature type codes this rule applies to (e.g. ["DepthContour", "DepthArea"]).
    /// Empty when <see cref="AlwaysApply"/> is true.
    /// </summary>
    public required IReadOnlyList<string> AppliesTo { get; init; }

    /// <summary>
    /// When true, this rule runs regardless of which feature types are present
    /// (e.g. meta-feature rules, global symbol setup).
    /// </summary>
    public bool AlwaysApply { get; init; }
}
