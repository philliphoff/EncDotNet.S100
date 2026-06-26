using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Marshals a route read/mutation onto the viewer's UI thread.
/// </summary>
/// <remarks>
/// <para>
/// MCP tools execute on Kestrel request threads, not the Avalonia UI
/// thread. The route MCP tools mutate the shared
/// <see cref="EncDotNet.S100.Viewer.Services.RoutesService"/>, whose
/// <c>Changed</c> event is consumed directly (without marshalling) by the
/// UI-bound <c>RoutesPanelViewModel</c> — which rebuilds
/// <c>ObservableCollection</c>s that Avalonia requires to be touched only
/// on the UI thread. Every route tool therefore performs its mutation and
/// result projection inside <see cref="InvokeAsync{T}"/> so the resulting
/// <c>Changed</c> fan-out (panel rebuild and overlay refresh) runs with UI
/// affinity.
/// </para>
/// <para>
/// Reads are marshalled too, so a tool always snapshots a consistent route
/// state rather than racing the interactive editor.
/// </para>
/// </remarks>
internal interface IRouteEditInvoker
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread and returns its
    /// result.
    /// </summary>
    /// <typeparam name="T">The action's result type.</typeparam>
    /// <param name="action">The work to run with UI affinity.</param>
    /// <returns>A task completing with the action's return value.</returns>
    Task<T> InvokeAsync<T>(Func<T> action);
}

/// <summary>
/// Production <see cref="IRouteEditInvoker"/> backed by
/// <see cref="Dispatcher.UIThread"/>.
/// </summary>
internal sealed class DispatcherRouteEditInvoker : IRouteEditInvoker
{
    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
