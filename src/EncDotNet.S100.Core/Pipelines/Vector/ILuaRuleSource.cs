using EncDotNet.S100.Pipelines.Vector.Lua;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply Lua portrayal rules
/// (S-100 Part 9A). A catalogue implements this interface as part of
/// <see cref="IVectorPortrayalCatalogue"/>; products that ship no Lua rules
/// (e.g. the GML/XSLT products) return an empty list from
/// <see cref="GetLuaSourceNamesAsync"/> and an empty
/// <see cref="ContextParameters"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the Core seam through which the generic
/// <see cref="Lua.LuaRuleExecutor"/> obtains everything it needs from a
/// catalogue: the manifest-declared list of Lua rule files and the raw
/// source for each one (for the MoonSharp <c>require()</c> loader and
/// <c>main.lua</c>), plus the declared context-parameter set (to initialise
/// the Lua side and to filter mariner overrides). It deliberately does not
/// expose a compiled script type, preserving per-render sandbox isolation.
/// </para>
/// <para>
/// The interface is purely asynchronous so the catalogue stays an
/// unopinionated data source. The MoonSharp <c>require()</c> module loader
/// is itself synchronous (we do not own MoonSharp), so the executor — not
/// the catalogue — is responsible for awaiting all source files into a
/// local snapshot dictionary before invoking the Lua engine, and capturing
/// that dictionary inside the sync loader callback.
/// </para>
/// </remarks>
public interface ILuaRuleSource
{
    /// <summary>
    /// Returns the bare file names (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>) of every Lua rule file declared in the
    /// portrayal catalogue manifest. The returned list is intended to be
    /// memoized by the catalogue; the executor uses it to drive
    /// <see cref="GetLuaSourceAsync"/> calls before kicking off the
    /// synchronous MoonSharp engine.
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw Lua source for the given bare file name inside the
    /// portrayal catalogue's <c>Rules/</c> directory (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>), or <see langword="null"/> if the file is
    /// not present — honouring the MoonSharp module loader's
    /// "missing module → return null" contract.
    /// </summary>
    ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The context parameters declared by the catalogue (S-100 Part 9A), used
    /// to initialise the Lua portrayal model and to filter
    /// <see cref="MarinerSettings"/> overrides to the names the catalogue
    /// actually understands. Empty for catalogues without Lua rules.
    /// </summary>
    IReadOnlyList<LuaContextParameter> ContextParameters { get; }
}
