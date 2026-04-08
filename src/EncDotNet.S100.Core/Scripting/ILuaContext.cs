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
    /// Calls a global Lua function and returns all return values.
    /// </summary>
    object?[] CallMultiReturn(string functionName, params object?[] args);
}
