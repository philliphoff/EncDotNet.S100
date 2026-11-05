using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp.Compatibility;

/// <summary>
/// Restores Lua 5.1/5.2 dispatch of the <c>__eq</c> metamethod.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deviation (Lua-CSharp 0.5.8).</b> Lua 5.2 §2.4 calls <c>__eq</c> only
/// when both operands are tables, or both full userdata, and their
/// <c>__eq</c> handlers are the same value. Lua-CSharp calls it whenever
/// either operand has a handler: for <c>nil</c>, for numbers, and for
/// tables with a different handler. The S-100 Part 9a framework depends on
/// the 5.2 rule: <c>PortrayalAPI.lua</c>'s <c>if av ~= nil</c> on a
/// ScaledDecimal otherwise reaches <c>ScaledDecimalCompare</c> and fails, and
/// <c>main.lua</c>'s <c>EqMetaMethodGuarantee</c> probe reports a
/// non-standard engine.
/// </para>
/// <para>
/// <b>Shim.</b> A prelude replaces the global <c>setmetatable</c>. When it
/// sees a metatable whose <c>__eq</c> is a function, it swaps in a guard that
/// returns <c>false</c> unless the operands pass the 5.2 test, and otherwise
/// calls the original. A metatable with no <c>__eq</c> yet gets a
/// <c>__newindex</c> watcher so a handler assigned later (as the probe does)
/// is guarded too. Guards are cached per original function, so two
/// metatables that share a handler still share a guard and still compare.
/// This covers the framework's ScaledDecimal and <c>unknownValue</c>
/// metatables and any other the rules create.
/// </para>
/// <para>
/// <b>Limits.</b> A handler assigned with <c>rawset</c>, or one that replaces
/// an existing <c>__eq</c> after <c>setmetatable</c>, isn't guarded. The
/// framework does neither.
/// </para>
/// <para>
/// <b>Removal.</b> Delete this class and its <c>Install</c> call once a
/// Lua-CSharp release dispatches <c>__eq</c> as Lua 5.2 does. The
/// <c>EqDispatch_*</c> tests in <c>EncDotNet.S100.Scripting.Tests</c> show
/// whether a release still needs it.
/// </para>
/// </remarks>
internal static class EqDispatchShim
{
    /// <summary>The prelude, run once in each new state.</summary>
    internal const string Prelude = """
        do
          local type, rawget, rawset, rawequal = type, rawget, rawset, rawequal
          local getmetatable, rawsetmetatable = getmetatable, setmetatable

          local guards = {}   -- original __eq -> its guard
          local isGuard = {}  -- guard -> true

          local function handler(value)
            local mt = getmetatable(value)
            if type(mt) ~= 'table' then return nil end
            return rawget(mt, '__eq')
          end

          local function guard(eq)
            if type(eq) ~= 'function' or isGuard[eq] then return eq end
            local g = guards[eq]
            if rawequal(g, nil) then
              g = function(a, b)
                local ta = type(a)
                if ta ~= type(b) or (ta ~= 'table' and ta ~= 'userdata') then return false end
                if not rawequal(handler(a), handler(b)) then return false end
                return eq(a, b)
              end
              guards[eq] = g
              isGuard[g] = true
            end
            return g
          end

          local watcher = {
            __newindex = function(mt, key, value)
              if key == '__eq' then value = guard(value) end
              rawset(mt, key, value)
            end
          }

          setmetatable = function(t, mt)
            if type(mt) == 'table' then
              local eq = rawget(mt, '__eq')
              if not rawequal(eq, nil) then
                if not isGuard[eq] then rawset(mt, '__eq', guard(eq)) end
              elseif rawequal(getmetatable(mt), nil) then
                rawsetmetatable(mt, watcher)
              end
            end
            return rawsetmetatable(t, mt)
          end
        end
        """;

    /// <summary>Installs the shim in <paramref name="state"/>.</summary>
    /// <param name="state">A state with the basic library open.</param>
    public static void Install(LuaState state) =>
        SyncBridge.Execute(state, Prelude, "=EqDispatchShim", CancellationToken.None);
}
