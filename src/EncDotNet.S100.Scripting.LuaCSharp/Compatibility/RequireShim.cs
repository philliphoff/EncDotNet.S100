using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp.Compatibility;

/// <summary>
/// Makes <c>require</c> record a module that returns nothing, as Lua 5.2
/// does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deviation (Lua-CSharp 0.5.8).</b> Lua 5.2 §6.3 stores <c>true</c> in
/// <c>package.loaded[name]</c> when a module's chunk returns no value, so the
/// next <c>require</c> returns at once. Lua-CSharp stores the
/// <c>nil</c> the chunk returned, which leaves the entry empty, so every
/// <c>require</c> of such a module runs its chunk again. The S-100 Part 9a
/// rule files return nothing: <c>main.lua</c>'s <c>require(feature.Code)</c>
/// recompiled a rule for every feature (64,000 loads instead of 226 on
/// US3WA1EF), and the repeated loads of <c>S100Scripting</c> created new
/// metatables that broke ScaledDecimal comparisons (#1278).
/// </para>
/// <para>
/// <b>Shim.</b> The global <c>require</c> is wrapped. A name already in
/// <c>package.loaded</c> returns its value without calling the original;
/// otherwise the original loads the module, and if that leaves the entry
/// empty the wrapper stores <c>true</c>.
/// </para>
/// <para>
/// <b>Limits.</b> Lua-CSharp also clears the entry when a chunk sets
/// <c>package.loaded[name]</c> itself and returns nothing, so such a module
/// gets <c>true</c> rather than the value it set. No catalogue does that.
/// </para>
/// <para>
/// <b>Removal.</b> Delete this class and its <c>Install</c> call once a
/// Lua-CSharp release records modules that return nothing. The
/// <c>Require_*</c> tests in <c>EncDotNet.S100.Scripting.Tests</c> show
/// whether a release still needs it.
/// </para>
/// </remarks>
internal static class RequireShim
{
    /// <summary>Installs the shim in <paramref name="state"/>.</summary>
    /// <param name="state">A state with the package library open.</param>
    public static void Install(LuaState state)
    {
        var original = state.Environment["require"];
        var loaded = state.LoadedModules;

        state.Environment["require"] = new LuaFunction("require", async (context, cancellationToken) =>
        {
            var name = context.GetArgument(0);
            if (loaded.TryGetValue(name, out var cached) && cached.Type != LuaValueType.Nil)
            {
                return context.Return(cached);
            }

            var results = await context.State.CallAsync(original, [name], cancellationToken);
            var module = results.Length > 0 ? results[0] : LuaValue.Nil;
            if (module.Type == LuaValueType.Nil)
            {
                module = loaded[name];
                if (module.Type == LuaValueType.Nil)
                {
                    module = true;
                    loaded[name] = module;
                }
            }

            return context.Return(module);
        });
    }
}
