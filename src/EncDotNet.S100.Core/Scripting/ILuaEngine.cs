namespace EncDotNet.S100.Scripting;

/// <summary>
/// Creates isolated Lua execution contexts for running portrayal catalogue scripts.
/// Implementations wrap a specific Lua runtime (e.g. MoonSharp, NLua) but are
/// consumed only through this abstraction so the core library stays dependency-free.
/// </summary>
public interface ILuaEngine
{
    /// <summary>
    /// Creates a new isolated Lua execution context.
    /// Each context has its own global table and loaded scripts.
    /// </summary>
    ILuaContext CreateContext();
}
