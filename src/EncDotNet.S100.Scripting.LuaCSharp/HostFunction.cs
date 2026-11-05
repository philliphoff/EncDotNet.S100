using System.Globalization;
using System.Runtime.CompilerServices;
using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// Wraps .NET delegates as Lua functions.
/// </summary>
/// <remarks>
/// <para>
/// The host APIs the portrayal providers register are a handful of
/// <c>Func</c>/<c>Action</c> shapes over <see cref="double"/>,
/// <see cref="string"/> and reference-type results. Each shape is matched
/// once, at registration, against a closed generic type, and the wrapper
/// calls the delegate directly: no reflection, no
/// <see cref="Delegate.DynamicInvoke"/> and no argument array on the call
/// path. <c>Func</c> is covariant in its result, so
/// <c>Func&lt;double, object?&gt;</c> also matches a
/// <c>Func&lt;double, List&lt;object&gt;&gt;</c>.
/// </para>
/// <para>
/// Any other delegate falls back to <see cref="Delegate.DynamicInvoke"/>. It
/// works, but it's slow, so add the shape here if a hot host function needs
/// it.
/// </para>
/// <para>
/// Arguments convert as the MoonSharp adapter converts them: a missing or
/// <c>nil</c> argument becomes the parameter type's default, a number passed
/// for a string parameter is formatted in the invariant culture, and a
/// string passed for a number is parsed.
/// </para>
/// </remarks>
internal static class HostFunction
{
    /// <summary>Wraps <paramref name="del"/> as a Lua function.</summary>
    /// <param name="del">The delegate.</param>
    /// <returns>The function.</returns>
    public static LuaFunction Wrap(Delegate del) => del switch
    {
        Action a => Action0(a),
        Action<string> a => Action1(a),
        Action<double> a => Action1(a),
        Action<string, string> a => Action2(a),
        Func<object?> f => Func0(f),
        Func<double, object?> f => Func1(f),
        Func<string, object?> f => Func1(f),
        Func<double, string, object?> f => Func2(f),
        Func<string, string, object?> f => Func2(f),
        Func<double, string, string, object?> f => Func3(f),
        Func<string, string, string, object?> f => Func3(f),
        Func<double, string, string, double> f => Func3(f),
        Func<string, string, string, bool> f => Func3(f),
        Func<double> f => Func0(f),
        Func<bool> f => Func0(f),
        _ => Dynamic(del),
    };

    private static LuaFunction Action0(Action fn) =>
        new((context, _) =>
        {
            fn();
            return new(context.Return());
        });

    private static LuaFunction Action1<T1>(Action<T1> fn) =>
        new((context, _) =>
        {
            fn(Arg<T1>(context, 0));
            return new(context.Return());
        });

    private static LuaFunction Action2<T1, T2>(Action<T1, T2> fn) =>
        new((context, _) =>
        {
            fn(Arg<T1>(context, 0), Arg<T2>(context, 1));
            return new(context.Return());
        });

    private static LuaFunction Func0<TResult>(Func<TResult> fn) =>
        new((context, _) => new(context.Return(Result(fn()))));

    private static LuaFunction Func1<T1, TResult>(Func<T1, TResult> fn) =>
        new((context, _) => new(context.Return(Result(fn(Arg<T1>(context, 0))))));

    private static LuaFunction Func2<T1, T2, TResult>(Func<T1, T2, TResult> fn) =>
        new((context, _) => new(context.Return(Result(fn(Arg<T1>(context, 0), Arg<T2>(context, 1))))));

    private static LuaFunction Func3<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> fn) =>
        new((context, _) => new(context.Return(Result(
            fn(Arg<T1>(context, 0), Arg<T2>(context, 1), Arg<T3>(context, 2))))));

    private static LuaFunction Dynamic(Delegate del)
    {
        var parameters = del.Method.GetParameters();
        return new((context, _) =>
        {
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var value = LuaMarshal.FromLua(ArgValue(context, i));
                args[i] = value is null ? null : Convert(value, parameters[i].ParameterType);
            }

            return new(context.Return(LuaMarshal.ToLua(del.DynamicInvoke(args))));
        });
    }

    private static LuaValue ArgValue(in LuaFunctionExecutionContext context, int index) =>
        index < context.ArgumentCount ? context.Arguments[index] : LuaValue.Nil;

    /// <summary>
    /// Reads argument <paramref name="index"/> as <typeparamref name="T"/>.
    /// The type tests are constants for each instantiation, so the JIT keeps
    /// only the branch that applies.
    /// </summary>
    private static T Arg<T>(in LuaFunctionExecutionContext context, int index)
    {
        var value = ArgValue(context, index);
        if (typeof(T) == typeof(double))
        {
            var number = ToDouble(value);
            return Unsafe.As<double, T>(ref number);
        }

        if (typeof(T) == typeof(string))
        {
            var text = value.Type switch
            {
                LuaValueType.Nil => null,
                LuaValueType.String => value.Read<string>(),
                LuaValueType.Number => value.Read<double>().ToString(CultureInfo.InvariantCulture),
                LuaValueType.Boolean => value.Read<bool>() ? bool.TrueString : bool.FalseString,
                _ => (string)Convert(NonNil(value), typeof(string)),
            };
            return Unsafe.As<string?, T>(ref text);
        }

        if (value.Type == LuaValueType.Nil)
        {
            // The parameter type's default, as for a missing argument in
            // MoonSharp: null for a reference type, which the delegate's
            // parameter may not declare as nullable.
            return default!;
        }

        return (T)Convert(NonNil(value), typeof(T));
    }

    private static double ToDouble(LuaValue value) => value.Type switch
    {
        LuaValueType.Number => value.Read<double>(),
        LuaValueType.Nil => 0,
        LuaValueType.Boolean => value.Read<bool>() ? 1 : 0,
        _ => (double)Convert(NonNil(value), typeof(double)),
    };

    private static LuaValue Result<T>(T value)
    {
        if (typeof(T) == typeof(double))
        {
            return Unsafe.As<T, double>(ref value);
        }

        if (typeof(T) == typeof(bool))
        {
            return Unsafe.As<T, bool>(ref value);
        }

        return LuaMarshal.ToLua(value);
    }

    /// <summary>
    /// Converts a marshalled argument to a parameter type, as the MoonSharp
    /// adapter does.
    /// </summary>
    private static object Convert(object value, Type targetType)
    {
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is double d)
        {
            if (targetType == typeof(int)) return (int)d;
            if (targetType == typeof(float)) return (float)d;
            if (targetType == typeof(long)) return (long)d;
            if (targetType == typeof(decimal)) return (decimal)d;
        }

        return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    // FromLua returns null only for nil.
    private static object NonNil(LuaValue value) =>
        LuaMarshal.FromLua(value) ?? throw new ArgumentException("Expected a value other than nil.", nameof(value));
}
