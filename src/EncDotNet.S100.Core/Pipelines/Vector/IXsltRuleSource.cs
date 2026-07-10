using System.Xml.Xsl;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply XSLT portrayal rules
/// (S-100 Part 9 §9.4). A catalogue implements this interface when at least
/// one of its loaded rules has <see cref="PortrayalRuleType.Xslt"/>.
/// </summary>
/// <remarks>
/// <para>
/// The single member is asynchronous because uncached lookups perform I/O
/// against the underlying <see cref="IAssetSource"/> (the XSLT XML must be
/// read and compiled). Implementations are expected to memoize compiled
/// transforms, so subsequent accesses for the same rule complete
/// synchronously through the <see cref="ValueTask{TResult}"/> fast path.
/// </para>
/// <para>
/// Implementations should throw <see cref="PortrayalAssetNotFoundException"/> from
/// <see cref="GetCompiledRuleAsync"/> when the named rule is not present in
/// the loaded catalogue, just as <see cref="IPortrayalAssetSource"/>
/// methods do for missing symbols, line styles, or area fills. The
/// "this product never has XSLT" assertion belongs in the rule list,
/// not in the type system.
/// </para>
/// </remarks>
public interface IXsltRuleSource
{
    /// <summary>Returns the compiled XSLT transform for the named rule.</summary>
    ValueTask<XslCompiledTransform> GetCompiledRuleAsync(string ruleName, CancellationToken cancellationToken = default);
}
