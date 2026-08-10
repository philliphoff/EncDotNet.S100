using Avalonia.Threading;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Viewer.Services.McpCapabilities;

/// <summary>
/// Adapts the viewer's <see cref="GlobalTimeService"/> to the shared
/// <see cref="ITimeController"/> that backs the <c>set_time_step</c> tool.
/// </summary>
/// <remarks>
/// The read side maps straight through — <see cref="Current"/> to the global
/// map clock and <see cref="AvailableSteps"/> to the aggregated samples (empty
/// when nothing time-aware is loaded, which the tool surfaces as
/// <c>host_not_ready</c>). <see cref="SetTimeAsync"/> marshals the clock change
/// onto the UI thread, matching the viewer's own load/unload path: the
/// <see cref="GlobalTimeService.CurrentTimeChanged"/> event drives UI-bound
/// subscribers (the timeline, toolbars), so the set must originate on the UI
/// thread.
/// </remarks>
/// <param name="globalTime">The viewer's aggregate timeline service.</param>
/// <param name="dispatcher">
/// Test seam marshalling an action onto the UI thread; defaults to
/// <see cref="Dispatcher.UIThread"/> in production.
/// </param>
internal sealed class ViewerTimeController(
    GlobalTimeService globalTime,
    Func<Action, Task>? dispatcher = null)
    : ITimeController
{
    private readonly GlobalTimeService _globalTime = globalTime
        ?? throw new ArgumentNullException(nameof(globalTime));

    private readonly Func<Action, Task> _dispatcher = dispatcher ?? (action =>
    {
        var op = Dispatcher.UIThread.InvokeAsync(action);
        return op.GetTask();
    });

    /// <inheritdoc />
    public DateTime? Current => _globalTime.CurrentTime;

    /// <inheritdoc />
    public IReadOnlyList<DateTime> AvailableSteps => _globalTime.AllSamples;

    /// <inheritdoc />
    public Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _dispatcher(() => _globalTime.SetCurrentTime(time));
    }
}
