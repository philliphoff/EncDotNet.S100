using System.Xml.Xsl;

namespace EncDotNet.S100.Pipelines.Vector;

public interface IVectorPortrayalCatalogue : IPortrayalCatalogue
{
    XslCompiledTransform GetRule(string ruleName);
    Script GetLuaScript(string scriptName);
    SvgSymbol GetSymbol(string symbolName);
    LineStyle GetLineStyle(string name);
    AreaFill GetAreaFill(string name);
    ViewingGroupController ViewingGroups { get; }

    /// <summary>
    /// Given a feature, determine which portrayal rule to apply.
    /// Returns the rule file name (XSLT or Lua) to execute.
    /// </summary>
    string ResolveRule(string featureType, IReadOnlyDictionary<string, object?> attributes);
}
 