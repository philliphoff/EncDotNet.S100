using System.Diagnostics;
using System.Globalization;
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
            var luaArgs = args.Select(MarshalToLua).ToArray();
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
            var luaArgs = args.Select(MarshalToLua).ToArray();
            var result = WithInvariantCulture(() => _script.Call(fn, luaArgs));

            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "ok"));

            if (result.Type == DataType.Tuple)
            {
                return result.Tuple.Select(MarshalFromLua).ToArray();
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
    private CallbackFunction WrapDelegate(Delegate del)
    {
        return new CallbackFunction((ctx, callArgs) =>
        {
            var parameters = del.Method.GetParameters();
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
