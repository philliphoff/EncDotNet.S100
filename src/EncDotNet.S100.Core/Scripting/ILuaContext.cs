namespace EncDotNet.S100.Scripting;

/// <summary>
/// An isolated Lua execution environment that can load scripts, set/get globals,
/// and call functions. Corresponds to a single Lua state.
/// </summary>
public interface ILuaContext : IDisposable
{
    /// <summary>
    /// Executes a chunk of Lua source code in this context.
    /// Any functions or globals defined by the chunk become available
    /// for subsequent calls.
    /// </summary>
    void Execute(string source);

    /// <summary>
    /// Executes a chunk of Lua source code under a chunk name, as a file
    /// loaded by Lua would be: error messages and a line hook
    /// (<see cref="TrySetLineHook"/>) name <paramref name="chunkName"/>
    /// instead of the source.
    /// </summary>
    /// <remarks>
    /// The default implementation ignores the name and calls
    /// <see cref="Execute(string)"/>.
    /// </remarks>
    /// <param name="source">The Lua source.</param>
    /// <param name="chunkName">The chunk's name, for example <c>main.lua</c>.</param>
    void Execute(string source, string chunkName) => Execute(source);

    /// <summary>
    /// Installs or removes a hook that is called before each new line of Lua
    /// runs, with the name of the chunk the line is in and its line number,
    /// both as Lua's error messages give them. A tool that traces which
    /// lines of a portrayal rule ran installs one; rendering never does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chunk name is a module's name with its <c>.lua</c> extension
    /// (<c>require 'TOPMAR02'</c> runs in <c>TOPMAR02.lua</c>), the name
    /// given to <see cref="Execute(string, string)"/>, or, for a chunk run
    /// by <see cref="Execute(string)"/>, its source. Lines are numbered from
    /// 1, counting line feeds in the source the chunk was loaded from.
    /// </para>
    /// <para>
    /// Lines run in coroutines the script creates after the hook is
    /// installed are reported too. Lines run in coroutines created before
    /// it might not be. The default implementation supports no hook and
    /// returns <see langword="false"/>.
    /// </para>
    /// </remarks>
    /// <param name="hook">
    /// The hook, called with the chunk name and line number, or
    /// <see langword="null"/> to remove the hook.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the engine supports a line hook and it was
    /// installed or removed; <see langword="false"/> if the engine has none.
    /// </returns>
    bool TrySetLineHook(Action<string, int>? hook) => false;

    /// <summary>
    /// Registers a callback that resolves module names to Lua source code,
    /// enabling the Lua <c>require()</c> function. When a script calls
    /// <c>require("moduleName")</c>, the callback is invoked with the module
    /// name; it should return the source code or <c>null</c> if not found.
    /// </summary>
    void SetModuleLoader(Func<string, string?> loader);

    /// <summary>
    /// Sets a global variable in the Lua environment.
    /// <para>Supported .NET → Lua type mappings:</para>
    /// <list type="bullet">
    ///   <item><c>null</c> → <c>nil</c></item>
    ///   <item><see cref="bool"/> → <c>boolean</c></item>
    ///   <item>Numeric types (<see cref="double"/>, <see cref="int"/>, etc.) → <c>number</c></item>
    ///   <item><see cref="string"/> → <c>string</c></item>
    ///   <item><see cref="Delegate"/> (Action/Func) → callable <c>function</c></item>
    ///   <item><see cref="IReadOnlyDictionary{TKey, TValue}"/> → <c>table</c></item>
    /// </list>
    /// </summary>
    void SetGlobal(string name, object? value);

    /// <summary>
    /// Gets the value of a global variable, converted to the closest .NET type.
    /// Returns <c>null</c> for Lua <c>nil</c>.
    /// </summary>
    object? GetGlobal(string name);

    /// <summary>
    /// Calls a global Lua function by name with the given arguments.
    /// Returns the first return value, or <c>null</c> if the function returns nothing.
    /// </summary>
    object? Call(string functionName, params object?[] args);

    /// <summary>
    /// Calls a global Lua function by name with the given arguments, and
    /// stops it when <paramref name="cancellationToken"/> is cancelled.
    /// Returns the first return value, or <c>null</c> if the function returns
    /// nothing.
    /// </summary>
    /// <remarks>
    /// An engine that can interrupt a running script checks the token between
    /// instructions and throws an <see cref="OperationCanceledException"/>.
    /// The default implementation ignores the token, so a caller that needs
    /// to stop a long call on every engine must also check it from a host
    /// function the script calls.
    /// </remarks>
    /// <param name="functionName">The global function's name.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The first return value, or <c>null</c>.</returns>
    object? CallCancellable(string functionName, CancellationToken cancellationToken, params object?[] args) =>
        Call(functionName, args);

    /// <summary>
    /// Calls a global Lua function and returns all return values.
    /// </summary>
    object?[] CallMultiReturn(string functionName, params object?[] args);

    /// <summary>
    /// Describes an exception thrown by this context, including the Lua source
    /// location when the engine records one.
    /// </summary>
    string DescribeError(Exception exception);
}
