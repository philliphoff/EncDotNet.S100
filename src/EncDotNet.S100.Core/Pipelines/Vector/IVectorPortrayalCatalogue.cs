namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Portrayal catalogue for vector (S-101-style) products. Composes the
/// rule-list and viewing-group surface required by <see cref="VectorPipeline"/>
/// with the three capability interfaces that supply rules and assets:
/// <see cref="IXsltRuleSource"/>, <see cref="ILuaRuleSource"/>, and
/// <see cref="IPortrayalAssetSource"/>.
/// </summary>
/// <remarks>
/// Concrete catalogues (e.g. S-101, S-124, S-129, S-421) implement all
/// capability interfaces. The set of rule kinds actually serviced by a
/// catalogue is determined at run time by inspecting <see cref="Rules"/>:
/// the pipeline only requests an XSLT transform or Lua script for a rule
/// whose <see cref="PortrayalRule.Type"/> advertises that rule kind.
/// </remarks>
public interface IVectorPortrayalCatalogue
    : IPortrayalCatalogue, IXsltRuleSource, ILuaRuleSource, IPortrayalAssetSource
{
    /// <summary>
    /// All portrayal rules defined in the catalogue, ordered by
    /// <see cref="PortrayalRule.ExecutionOrder"/>.
    /// </summary>
    IReadOnlyList<PortrayalRule> Rules { get; }

    /// <summary>Controls which viewing groups are currently visible.</summary>
    ViewingGroupController ViewingGroups { get; }

    /// <summary>
    /// Tracks the active S-100 Part 9 display-mode id for the
    /// catalogue. When the controller's active id changes the
    /// catalogue is responsible for resolving the corresponding
    /// viewing-group membership and pushing it into
    /// <see cref="ViewingGroups"/> via
    /// <see cref="ViewingGroupController.SetActiveModeMembership"/>.
    /// </summary>
    DisplayModeController DisplayModes { get; }
}
