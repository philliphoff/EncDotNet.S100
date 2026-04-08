using System.Xml.Xsl;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Portrayal catalogue for vector (S-101-style) products.
/// Provides rules, compiled XSLT transforms, Lua scripts, and symbolisation
/// resources to the <see cref="VectorPipeline"/>.
/// </summary>
public interface IVectorPortrayalCatalogue : IPortrayalCatalogue
{
    /// <summary>
    /// All portrayal rules defined in the catalogue, ordered by
    /// <see cref="PortrayalRule.ExecutionOrder"/>.
    /// </summary>
    IReadOnlyList<PortrayalRule> Rules { get; }

    /// <summary>Returns the compiled XSLT transform for the named rule.</summary>
    XslCompiledTransform GetCompiledRule(string ruleName);

    /// <summary>Returns the Lua script source for the named rule.</summary>
    Script GetLuaScript(string scriptName);

    /// <summary>Resolves an SVG symbol by name from the catalogue resources.</summary>
    SvgSymbol GetSymbol(string symbolName);

    /// <summary>Resolves a line style by name from the catalogue resources.</summary>
    LineStyle GetLineStyle(string name);

    /// <summary>Resolves an area fill by name from the catalogue resources.</summary>
    AreaFill GetAreaFill(string name);

    /// <summary>Controls which viewing groups are currently visible.</summary>
    ViewingGroupController ViewingGroups { get; }
}
 