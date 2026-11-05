using System.Diagnostics;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Scripting.LuaCSharp.Diagnostics;
using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// <see cref="ILuaContext"/> implementation backed by a Lua-CSharp
/// <see cref="LuaState"/>.
/// </summary>
/// <remarks>
/// Lua-CSharp's API is asynchronous; this context completes each call
/// synchronously through <see cref="SyncBridge"/>, as host functions are
/// synchronous. Lua-CSharp formats numbers with the invariant culture, so
/// unlike the MoonSharp adapter this context needn't switch the thread's
/// culture around each call.
/// </remarks>
internal sealed class LuaCSharpLuaContext : ILuaContext
{
    private readonly LuaState _state;
    private bool _disposed;

    internal LuaCSharpLuaContext(LuaState state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public void Execute(string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(source);

        // The source is its own chunk name, as for Lua's load(): errors show
        // it as [string "first line..."].
        SyncBridge.Execute(_state, source, source, CancellationToken.None);
    }

    /// <inheritdoc />
    public string DescribeError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        // Lua-CSharp's message names the chunk and line only for some errors
        // (an error() call, a parse error), so add the Lua stack, whose lines
        // read "chunk:line: in function ...", as Lua's own traceback does.
        return exception switch
        {
            LuaRuntimeException { LuaTraceback: { } traceback } lua => $"{lua.Message}\n{traceback}",
            LuaCanceledException { LuaTraceback: { } traceback } canceled => $"{canceled.Message}\n{traceback}",
            _ => exception.Message,
        };
    }

    /// <inheritdoc />
    public void SetModuleLoader(Func<string, string?> loader)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(loader);
        _state.ModuleLoader = new DelegateModuleLoader(loader);
    }

    /// <inheritdoc />
    public void SetGlobal(string name, object? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _state.Environment[name] = LuaMarshal.ToLua(value);
    }

    /// <inheritdoc />
    public object? GetGlobal(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return LuaMarshal.FromLua(_state.Environment[name]);
    }

    /// <inheritdoc />
    public object? Call(string functionName, params object?[] args) =>
        CallCancellable(functionName, CancellationToken.None, args);

    /// <inheritdoc />
    public object? CallCancellable(string functionName, CancellationToken cancellationToken, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(functionName);
        ArgumentNullException.ThrowIfNull(args);

        return Invoke(functionName, args, cancellationToken, static results =>
            results.Length > 0 ? LuaMarshal.FromLua(results[0]) : null);
    }

    /// <inheritdoc />
    public object?[] CallMultiReturn(string functionName, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(functionName);
        ArgumentNullException.ThrowIfNull(args);

        return Invoke(functionName, args, CancellationToken.None, static results =>
        {
            // A call that returns nothing reads as one nil, as in MoonSharp.
            if (results.Length == 0)
            {
                return new object?[] { null };
            }

            var values = new object?[results.Length];
            for (var i = 0; i < results.Length; i++)
            {
                values[i] = LuaMarshal.FromLua(results[i]);
            }

            return values;
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    private delegate T ResultReader<T>(ReadOnlySpan<LuaValue> results);

    private T Invoke<T>(string functionName, object?[] args, CancellationToken cancellationToken, ResultReader<T> read)
    {
        var function = _state.Environment[functionName];
        if (function.Type == LuaValueType.Nil)
        {
            throw new InvalidOperationException($"Lua global '{functionName}' is nil or not defined.");
        }

        // Per-call activities are gated by Source listening: when the host
        // doesn't subscribe (the common case) StartActivity returns null and
        // there is no overhead. The counter and histogram are gated likewise.
        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.rule.invoke");
        activity?.SetTag(TelemetryTags.LuaRule, functionName);
        var start = Stopwatch.GetTimestamp();
        var ruleTag = new KeyValuePair<string, object?>(TelemetryTags.LuaRule, functionName);
        var stack = _state.Stack;
        var functionIndex = stack.Count;
        try
        {
            // Arguments go on the Lua stack, so a call allocates no argument
            // or result array of its own.
            _state.Push(function);
            foreach (var arg in args)
            {
                _state.Push(LuaMarshal.ToLua(arg));
            }

            var count = SyncBridge.Complete(_state.CallAsync(functionIndex, cancellationToken));
            using var results = _state.ReadStack(count);
            var value = read(results.AsSpan());
            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "ok"));
            return value;
        }
        catch
        {
            Telemetry.InvokeCount.Add(1, ruleTag, new KeyValuePair<string, object?>(TelemetryTags.Result, "error"));
            activity?.SetStatus(ActivityStatusCode.Error);
            stack.PopUntil(functionIndex);
            throw;
        }
        finally
        {
            Telemetry.InvokeDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, ruleTag);
        }
    }
}
