using System.Collections;
using System.Globalization;
using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// Converts values between .NET and Lua-CSharp's <see cref="LuaValue"/>,
/// following the <see cref="ILuaContext"/> contract and matching the MoonSharp
/// adapter's conversions so both engines hand the rules the same values.
/// </summary>
internal static class LuaMarshal
{
    /// <summary>Converts a .NET value to a Lua value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>
    /// <c>nil</c>, a boolean, number or string; a function for a delegate; a
    /// table for a dictionary or list; otherwise light userdata.
    /// </returns>
    public static LuaValue ToLua(object? value) => value switch
    {
        null => LuaValue.Nil,
        LuaValue lua => lua,
        bool b => b,
        double d => d,
        string s => s,
        int i => i,
        long l => l,
        float f => f,
        LuaTable table => table,
        LuaFunction function => function,
        Delegate del => HostFunction.Wrap(del),
        IReadOnlyDictionary<string, object?> dict => ToTable(dict),
        IList<object> list => ToTable(list),
        IConvertible convertible when IsNumber(convertible) =>
            convertible.ToDouble(CultureInfo.InvariantCulture),
        IEnumerable sequence => ToTable(sequence),
        _ => LuaValue.FromObject(value),
    };

    /// <summary>Converts a Lua value to the closest .NET value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>
    /// <see langword="null"/>, <see cref="bool"/>, <see cref="double"/> or
    /// <see cref="string"/>; for a table, a <see cref="List{T}"/> when its keys
    /// are exactly 1..n and a <see cref="Dictionary{TKey, TValue}"/>
    /// otherwise; for anything else, the Lua-CSharp object.
    /// </returns>
    public static object? FromLua(LuaValue value) => value.Type switch
    {
        LuaValueType.Nil => null,
        LuaValueType.Boolean => value.Read<bool>(),
        LuaValueType.Number => value.Read<double>(),
        LuaValueType.String => value.Read<string>(),
        LuaValueType.Table => FromTable(value.Read<LuaTable>()),
        _ => value.Read<object>(),
    };

    /// <summary>
    /// Formats a Lua key or value as text: numbers in the invariant culture,
    /// strings as they are.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Its text.</returns>
    public static string ToText(LuaValue value) => value.Type switch
    {
        LuaValueType.String => value.Read<string>(),
        LuaValueType.Number => value.Read<double>().ToString(CultureInfo.InvariantCulture),
        LuaValueType.Boolean => value.Read<bool>() ? "true" : "false",
        _ => value.ToString(),
    };

    private static object FromTable(LuaTable table)
    {
        // A list when the keys are exactly 1..n, as the MoonSharp adapter does.
        var count = 0;
        var isList = true;
        foreach (var (key, _) in table)
        {
            count++;
            if (isList && !(key.TryRead<double>(out var index) && index >= 1 && index == Math.Floor(index)))
            {
                isList = false;
            }
        }

        if (isList && count > 0)
        {
            var list = new List<object?>(count);
            for (var i = 1; i <= count; i++)
            {
                var item = table[i];
                if (item.Type == LuaValueType.Nil)
                {
                    isList = false;
                    break;
                }

                list.Add(FromLua(item));
            }

            if (isList)
            {
                return list;
            }
        }

        var dict = new Dictionary<string, object?>(count);
        foreach (var (key, item) in table)
        {
            dict[ToText(key)] = FromLua(item);
        }

        return dict;
    }

    private static LuaTable ToTable(IReadOnlyDictionary<string, object?> dict)
    {
        var table = new LuaTable(0, dict.Count);
        foreach (var (key, value) in dict)
        {
            table[key] = ToLua(value);
        }

        return table;
    }

    private static LuaTable ToTable(IList<object> list)
    {
        var table = new LuaTable(list.Count, 0);
        for (var i = 0; i < list.Count; i++)
        {
            table[i + 1] = ToLua(list[i]);
        }

        return table;
    }

    private static LuaTable ToTable(IEnumerable sequence)
    {
        var table = new LuaTable();
        var i = 1;
        foreach (var item in sequence)
        {
            table[i++] = ToLua(item);
        }

        return table;
    }

    private static bool IsNumber(IConvertible value) => value.GetTypeCode() switch
    {
        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
            or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single
            or TypeCode.Double or TypeCode.Decimal => true,
        _ => false,
    };
}
