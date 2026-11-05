using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// An <see cref="ILuaModuleLoader"/> that asks a host callback for a module's
/// source, so <c>require</c> resolves modules from wherever the host keeps
/// them (for portrayal, the catalogue's rule files) and never from disk.
/// </summary>
/// <param name="loader">Returns a module's Lua source, or <see langword="null"/> when there's no such module.</param>
internal sealed class DelegateModuleLoader(Func<string, string?> loader) : ILuaModuleLoader
{
    /// <inheritdoc />
    public bool Exists(string moduleName) => loader(moduleName) is not null;

    /// <inheritdoc />
    public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        var source = loader(moduleName)
            ?? throw new LuaModuleNotFoundException(moduleName);

        // '@' tells Lua to show the name as is in errors, as for a file.
        return new(new LuaModule(ChunkName(moduleName), source));
    }

    private static string ChunkName(string moduleName) =>
        moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? $"@{moduleName}"
            : $"@{moduleName}.lua";
}
