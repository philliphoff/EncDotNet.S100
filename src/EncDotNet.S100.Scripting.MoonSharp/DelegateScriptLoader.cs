using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace EncDotNet.S100.Scripting.MoonSharp;

/// <summary>
/// A MoonSharp <see cref="IScriptLoader"/> that delegates module resolution
/// to a <see cref="Func{String, String}"/> callback, allowing the host to
/// supply Lua source code for <c>require()</c> calls.
/// </summary>
internal sealed class DelegateScriptLoader : ScriptLoaderBase
{
    private readonly Func<string, string?> _loader;

    public DelegateScriptLoader(Func<string, string?> loader)
    {
        _loader = loader;
        IgnoreLuaPathGlobal = true;
        ModulePaths = ["?", "?.lua"];
    }

    public override bool ScriptFileExists(string name) => _loader(name) is not null;

    public override object LoadFile(string file, Table globalContext)
    {
        return _loader(file)
            ?? throw new ScriptRuntimeException($"Module '{file}' not found.");
    }
}
