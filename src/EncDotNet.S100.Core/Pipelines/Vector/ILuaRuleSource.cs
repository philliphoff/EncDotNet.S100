using EncDotNet.S100.Pipelines.Vector.Lua;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply Lua portrayal rules
/// (S-100 Part 9A). A catalogue implements this interface as part of
/// <see cref="IVectorPortrayalCatalogue"/>; products that ship no Lua rules
/// (e.g. the GML/XSLT products) return <see langword="null"/> from
/// <see cref="GetLuaSource"/> and an empty <see cref="ContextParameters"/>.
/// </summary>
/// <remarks>
/// This is the Core seam through which the generic
/// <see cref="Lua.LuaRuleExecutor"/> obtains everything it needs from a
/// catalogue: raw module source (for the MoonSharp <c>require()</c> loader and
/// <c>main.lua</c>) and the declared context-parameter set (to initialise the
/// Lua side and to filter mariner overrides). It deliberately does not expose a
/// compiled script type, preserving per-render sandbox isolation.
/// </remarks>
public interface ILuaRuleSource
{
    /// <summary>
    /// Returns the raw Lua source for the given bare file name inside the
    /// portrayal catalogue's <c>Rules/</c> directory (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>), or <see langword="null"/> if the file is
    /// not present — honouring the MoonSharp module loader's
    /// "missing module → return null" contract.
    /// </summary>
    string? GetLuaSource(string fileName);

    /// <summary>
    /// The context parameters declared by the catalogue (S-100 Part 9A), used
    /// to initialise the Lua portrayal model and to filter
    /// <see cref="MarinerSettings"/> overrides to the names the catalogue
    /// actually understands. Empty for catalogues without Lua rules.
    /// </summary>
    IReadOnlyList<LuaContextParameter> ContextParameters { get; }
}
