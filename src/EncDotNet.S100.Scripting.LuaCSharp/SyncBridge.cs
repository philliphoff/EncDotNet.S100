using Lua;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// Completes Lua-CSharp's asynchronous calls for the synchronous
/// <see cref="ILuaContext"/>.
/// </summary>
/// <remarks>
/// Lua-CSharp's API returns <see cref="ValueTask{TResult}"/>, but a call only
/// suspends when a host function awaits something that isn't finished. Every
/// host function this adapter registers is synchronous, so every call
/// completes before it returns, and the result is read without blocking.
/// A call that didn't complete would mean an asynchronous host function had
/// been registered; that's a bug, so it throws rather than blocking a thread
/// on it.
/// </remarks>
internal static class SyncBridge
{
    /// <summary>Returns the result of a call that completed synchronously.</summary>
    /// <param name="task">The call.</param>
    /// <returns>Its result.</returns>
    /// <exception cref="InvalidOperationException">The call didn't complete.</exception>
    public static T Complete<T>(ValueTask<T> task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException(
                "A Lua call suspended. ILuaContext is synchronous, so host functions must not be asynchronous.");
        }

        // Rethrows the call's exception when it faulted.
        return task.Result;
    }

    /// <summary>Compiles and runs a chunk, discarding what it returns.</summary>
    /// <param name="state">The state to run it in.</param>
    /// <param name="source">The Lua source.</param>
    /// <param name="chunkName">The chunk name errors report, in Lua's <c>=</c>/<c>@</c> convention.</param>
    /// <param name="cancellationToken">Stops the chunk between instructions.</param>
    public static void Execute(LuaState state, string source, string chunkName, CancellationToken cancellationToken)
    {
        var closure = state.Load(source, chunkName);
        var count = Complete(state.RunAsync(closure, 0, cancellationToken));
        state.Pop(count);
    }
}
