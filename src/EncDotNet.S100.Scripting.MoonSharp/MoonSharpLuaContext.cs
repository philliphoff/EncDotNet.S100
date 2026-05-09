using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Scripting.MoonSharp.Diagnostics;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace EncDotNet.S100.Scripting.MoonSharp;

/// <summary>
/// <see cref="ILuaContext"/> implementation backed by a MoonSharp <see cref="Script"/> instance.
/// Handles bidirectional .NET ↔ Lua type marshalling.
/// </summary>
internal sealed class MoonSharpLuaContext : ILuaContext
{
    private readonly Script _script;
    private bool _disposed;

    internal MoonSharpLuaContext(Script script)
    {
        _script = script;
    }

    /// <inheritdoc />
    public void Execute(string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(source);
        WithInvariantCulture(() => _script.DoString(source));
    }

    /// <inheritdoc />
    public void SetModuleLoader(Func<string, string?> loader)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(loader);
        _script.Options.ScriptLoader = new DelegateScriptLoader(loader);
    }

    /// <inheritdoc />
    public void SetGlobal(string name, object? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _script.Globals[name] = MarshalToLua(value);
    }

    /// <inheritdoc />
    public object? GetGlobal(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var dyn = _script.Globals.Get(name);
        return MarshalFromLua(dyn);
    }

    /// <inheritdoc />
    public object? Call(string functionName, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        var fn = _script.Globals.Get(functionName);
        if (fn.IsNil())
            throw new InvalidOperationException($"Lua global '{functionName}' is nil or not defined.");

        // Per-call activities are gated by Source listening — when the host
        // does not subscribe (the common case) StartActivity returns null and
        // there is no overhead. Counter / histogram are similarly gated.
        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.rule.invoke");
        activity?.SetTag(TelemetryTags.LuaRule, functionName);
        var start = Stopwatch.GetTimestamp();
        var ruleTag = new KeyValuePair<string, object?>(TelemetryTags.LuaRule, functionName);
        try
        {
            var luaArgs = MarshalArgsToLua(args);
            var result = WithInvariantCulture(() => _script.Call(fn, luaArgs));
            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "ok"));
            return MarshalFromLua(result);
        }
        catch
        {
            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "error"));
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            Telemetry.InvokeDuration.Record(GetElapsedMs(start), ruleTag);
        }
    }

    /// <inheritdoc />
    public object?[] CallMultiReturn(string functionName, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        var fn = _script.Globals.Get(functionName);
        if (fn.IsNil())
            throw new InvalidOperationException($"Lua global '{functionName}' is nil or not defined.");

        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.rule.invoke");
        activity?.SetTag(TelemetryTags.LuaRule, functionName);
        var start = Stopwatch.GetTimestamp();
        var ruleTag = new KeyValuePair<string, object?>(TelemetryTags.LuaRule, functionName);
        try
        {
            var luaArgs = MarshalArgsToLua(args);
            var result = WithInvariantCulture(() => _script.Call(fn, luaArgs));

            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "ok"));

            if (result.Type == DataType.Tuple)
            {
                var tuple = result.Tuple;
                var arr = new object?[tuple.Length];
                for (int i = 0; i < tuple.Length; i++)
                    arr[i] = MarshalFromLua(tuple[i]);
                return arr;
            }

            return [MarshalFromLua(result)];
        }
        catch
        {
            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "error"));
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            Telemetry.InvokeDuration.Record(GetElapsedMs(start), ruleTag);
        }
    }

    private static double GetElapsedMs(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// Marshals a .NET argument array to MoonSharp <see cref="DynValue"/> values
    /// without LINQ. Returns <see cref="Array.Empty{T}"/> for zero-length input
    /// to avoid allocation.
    /// </summary>
    private DynValue[] MarshalArgsToLua(object?[] args)
    {
        if (args.Length == 0)
            return Array.Empty<DynValue>();

        var luaArgs = new DynValue[args.Length];
        for (int i = 0; i < args.Length; i++)
            luaArgs[i] = MarshalToLua(args[i]);
        return luaArgs;
    }

    /// <summary>
    /// Runs an action with <see cref="CultureInfo.InvariantCulture"/> as the current
    /// thread culture, ensuring Lua number-to-string conversions always use '.' as
    /// the decimal separator.
    /// </summary>
    private static void WithInvariantCulture(Action action)
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    private static T WithInvariantCulture<T>(Func<T> func)
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return func();
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    /// <summary>
    /// Converts a .NET object to a MoonSharp <see cref="DynValue"/> for injection into Lua.
    /// </summary>
    private DynValue MarshalToLua(object? value) => value switch
    {
        null => DynValue.Nil,
        bool b => DynValue.NewBoolean(b),
        double d => DynValue.NewNumber(d),
        float f => DynValue.NewNumber(f),
        int i => DynValue.NewNumber(i),
        long l => DynValue.NewNumber(l),
        string s => DynValue.NewString(s),
        Delegate del => DynValue.NewCallback(WrapDelegate(del)),
        IReadOnlyDictionary<string, object?> dict => MarshalDictionaryToTable(dict),
        IList<object> list => MarshalListToTable(list),
        _ => DynValue.FromObject(_script, value),
    };

    /// <summary>
    /// Converts a MoonSharp <see cref="DynValue"/> back to the closest .NET type.
    /// </summary>
    private static object? MarshalFromLua(DynValue dyn) => dyn.Type switch
    {
        DataType.Nil or DataType.Void => null,
        DataType.Boolean => dyn.Boolean,
        DataType.Number => dyn.Number,
        DataType.String => dyn.String,
        DataType.Table => MarshalTableFromLua(dyn.Table),
        _ => dyn.ToObject(),
    };

    /// <summary>
    /// Converts a Lua table to either a <see cref="List{T}"/> (array-like) or
    /// a <see cref="Dictionary{TKey, TValue}"/> (hash-like), matching the
    /// <see cref="ILuaContext"/> contract.
    /// </summary>
    private static object MarshalTableFromLua(Table table)
    {
        // If the table has sequential integer keys starting at 1, treat as an array.
        int length = table.Length;
        bool isArray = length > 0;

        if (isArray)
        {
            // Verify there are no non-integer keys beyond the array portion.
            int totalKeys = 0;
            foreach (var pair in table.Pairs)
                totalKeys++;

            isArray = totalKeys == length;
        }

        if (isArray)
        {
            var list = new List<object?>(length);
            for (int i = 1; i <= length; i++)
            {
                list.Add(MarshalFromLua(table.Get(i)));
            }
            return list;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var pair in table.Pairs)
        {
            string key = pair.Key.Type == DataType.Number
                ? pair.Key.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : pair.Key.CastToString() ?? pair.Key.ToString();
            dict[key] = MarshalFromLua(pair.Value);
        }
        return dict;
    }

    /// <summary>
    /// Wraps a .NET <see cref="Delegate"/> as a MoonSharp callback,
    /// handling argument and return value marshalling.
    /// </summary>
    /// <remarks>
    /// Hot-path optimisation: for known <c>Func&lt;&gt;</c> and <c>Action&lt;&gt;</c>
    /// arities the delegate is cast to its concrete type and invoked directly,
    /// bypassing <see cref="Delegate.DynamicInvoke"/> (which uses runtime
    /// reflection and boxes value-type arguments). The generic fallback path
    /// caches <c>GetParameters()</c> so reflection runs only once per delegate.
    /// </remarks>
    private CallbackFunction WrapDelegate(Delegate del)
    {
        // Try the fast typed-dispatch path first. If the delegate matches a
        // known Func<>/Action<> arity we avoid DynamicInvoke entirely.
        var fast = TryWrapTyped(del);
        if (fast is not null)
            return fast;

        // Fallback: generic reflection path. Cache parameter metadata once.
        var parameters = del.Method.GetParameters();
        return new CallbackFunction((ctx, callArgs) =>
        {
            var netArgs = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < callArgs.Count)
                {
                    var raw = MarshalFromLua(callArgs[i]);
                    netArgs[i] = ConvertArg(raw, parameters[i].ParameterType);
                }
            }

            var result = del.DynamicInvoke(netArgs);
            return MarshalToLua(result);
        });
    }

    /// <summary>
    /// Lookup table mapping generic type definitions to their <c>WrapXxxTyped</c>
    /// generic method infos. Built once per process via lazy reflection so the
    /// per-delegate registration path is a single dictionary lookup + one
    /// <see cref="MethodInfo.MakeGenericMethod"/> call.
    /// </summary>
    private static readonly Dictionary<Type, MethodInfo> TypedWrapMethods = BuildTypedWrapMethods();

    private static Dictionary<Type, MethodInfo> BuildTypedWrapMethods()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var self = typeof(MoonSharpLuaContext);
        return new Dictionary<Type, MethodInfo>
        {
            [typeof(Action<>)]       = self.GetMethod(nameof(WrapAction1Typed), flags)!,
            [typeof(Action<,>)]      = self.GetMethod(nameof(WrapAction2Typed), flags)!,
            [typeof(Func<>)]         = self.GetMethod(nameof(WrapFunc0Typed), flags)!,
            [typeof(Func<,>)]        = self.GetMethod(nameof(WrapFunc1Typed), flags)!,
            [typeof(Func<,,>)]       = self.GetMethod(nameof(WrapFunc2Typed), flags)!,
            [typeof(Func<,,,>)]      = self.GetMethod(nameof(WrapFunc3Typed), flags)!,
            [typeof(Func<,,,,>)]     = self.GetMethod(nameof(WrapFunc4Typed), flags)!,
        };
    }

    /// <summary>
    /// Attempts to wrap a delegate using direct typed invocation for known
    /// <c>Func&lt;&gt;</c> and <c>Action&lt;&gt;</c> arities (0–4 parameters).
    /// Returns <c>null</c> if the delegate type is not recognised.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="MethodInfo.MakeGenericMethod"/> once at registration time
    /// to create a strongly-typed wrapper. The per-call path then invokes the
    /// delegate directly — no <see cref="Delegate.DynamicInvoke"/>, no boxing
    /// of value-type arguments, and no per-call reflection.
    /// </remarks>
    private CallbackFunction? TryWrapTyped(Delegate del)
    {
        var type = del.GetType();

        if (type == typeof(Action))
        {
            var fn = (Action)del;
            return new CallbackFunction((ctx, args) => { fn(); return DynValue.Nil; });
        }

        if (!type.IsGenericType)
            return null;

        var genDef = type.GetGenericTypeDefinition();
        if (!TypedWrapMethods.TryGetValue(genDef, out var wrapMethod))
            return null;

        var typeArgs = type.GetGenericArguments();
        var concrete = wrapMethod.MakeGenericMethod(typeArgs);
        return (CallbackFunction)concrete.Invoke(this, [del])!;
    }

    // ── Typed wrappers ─────────────────────────────────────────────────
    // Each generic method is invoked once at registration time via
    // MakeGenericMethod. The returned CallbackFunction captures the
    // strongly-typed delegate, so the per-call path is a direct
    // invocation — no DynamicInvoke, no parameter-info reflection,
    // and no intermediate object[] allocation.

    private CallbackFunction WrapAction1Typed<T1>(Action<T1> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            fn(a1);
            return DynValue.Nil;
        });
    }

    private CallbackFunction WrapAction2Typed<T1, T2>(Action<T1, T2> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            var a2 = (T2)ConvertArg(MarshalFromLua(args.RawGet(1, false)), typeof(T2))!;
            fn(a1, a2);
            return DynValue.Nil;
        });
    }

    private CallbackFunction WrapFunc0Typed<TResult>(Func<TResult> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var result = fn();
            return MarshalToLua(result);
        });
    }

    private CallbackFunction WrapFunc1Typed<T1, TResult>(Func<T1, TResult> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            var result = fn(a1);
            return MarshalToLua(result);
        });
    }

    private CallbackFunction WrapFunc2Typed<T1, T2, TResult>(Func<T1, T2, TResult> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            var a2 = (T2)ConvertArg(MarshalFromLua(args.RawGet(1, false)), typeof(T2))!;
            var result = fn(a1, a2);
            return MarshalToLua(result);
        });
    }

    private CallbackFunction WrapFunc3Typed<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            var a2 = (T2)ConvertArg(MarshalFromLua(args.RawGet(1, false)), typeof(T2))!;
            var a3 = (T3)ConvertArg(MarshalFromLua(args.RawGet(2, false)), typeof(T3))!;
            var result = fn(a1, a2, a3);
            return MarshalToLua(result);
        });
    }

    private CallbackFunction WrapFunc4Typed<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, TResult> fn)
    {
        return new CallbackFunction((ctx, args) =>
        {
            var a1 = (T1)ConvertArg(MarshalFromLua(args.RawGet(0, false)), typeof(T1))!;
            var a2 = (T2)ConvertArg(MarshalFromLua(args.RawGet(1, false)), typeof(T2))!;
            var a3 = (T3)ConvertArg(MarshalFromLua(args.RawGet(2, false)), typeof(T3))!;
            var a4 = (T4)ConvertArg(MarshalFromLua(args.RawGet(3, false)), typeof(T4))!;
            var result = fn(a1, a2, a3, a4);
            return MarshalToLua(result);
        });
    }

    /// <summary>
    /// Converts a marshalled Lua value to the target .NET parameter type.
    /// </summary>
    private static object? ConvertArg(object? value, Type targetType)
    {
        if (value is null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        // Lua numbers come back as double — convert to the expected numeric type.
        if (value is double d)
        {
            if (targetType == typeof(int)) return (int)d;
            if (targetType == typeof(float)) return (float)d;
            if (targetType == typeof(long)) return (long)d;
            if (targetType == typeof(decimal)) return (decimal)d;
        }

        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private DynValue MarshalDictionaryToTable(IReadOnlyDictionary<string, object?> dict)
    {
        var table = new Table(_script);
        foreach (var (key, val) in dict)
        {
            table[key] = MarshalToLua(val);
        }
        return DynValue.NewTable(table);
    }

    private DynValue MarshalListToTable(IList<object> list)
    {
        var table = new Table(_script);
        for (int i = 0; i < list.Count; i++)
        {
            table[i + 1] = MarshalToLua(list[i]);
        }
        return DynValue.NewTable(table);
    }
}
