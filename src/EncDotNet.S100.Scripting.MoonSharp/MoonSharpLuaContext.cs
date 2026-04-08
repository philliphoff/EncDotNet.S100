using MoonSharp.Interpreter;

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
        _script.DoString(source);
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

        var luaArgs = args.Select(MarshalToLua).ToArray();
        var result = _script.Call(fn, luaArgs);
        return MarshalFromLua(result);
    }

    /// <inheritdoc />
    public object?[] CallMultiReturn(string functionName, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        var fn = _script.Globals.Get(functionName);
        if (fn.IsNil())
            throw new InvalidOperationException($"Lua global '{functionName}' is nil or not defined.");

        var luaArgs = args.Select(MarshalToLua).ToArray();
        var result = _script.Call(fn, luaArgs);

        if (result.Type == DataType.Tuple)
        {
            return result.Tuple.Select(MarshalFromLua).ToArray();
        }

        return [MarshalFromLua(result)];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
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
}
