using System.Diagnostics;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using SkiaSharp;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// One dataset to portray for a chart render: its processor, the context to
/// portray it with, and whether it is painted.
/// </summary>
/// <param name="Processor">
/// The dataset's processor; it must implement <see cref="IVectorPortrayalSource"/>
/// or <see cref="ICoveragePortrayalSource"/>.
/// </param>
/// <param name="Context">The palette, scales, time step, viewport and mariner settings to portray with.</param>
/// <param name="Active">Whether the dataset is painted; an inactive one is S-98 rule context only.</param>
public sealed record ChartPortrayalRequest(IDatasetProcessor Processor, RenderContext Context, bool Active = true);

/// <summary>Settings for <see cref="ChartRenderer.RenderTilesAsync{T}"/>.</summary>
public sealed record ChartTileJobOptions
{
    /// <summary>
    /// The most tile blocks rendered or waiting to be written at once, which
    /// bounds memory; <see langword="null"/> for twice the scheduler's workers.
    /// </summary>
    public int? MaxInFlight { get; init; }

    /// <summary>
    /// Added to each block's place in the list to give its urgency in the
    /// <see cref="WorkClass.Visible"/> class, so blocks of later calls queue
    /// behind earlier ones. Defaults to 0.
    /// </summary>
    public double UrgencyOffset { get; init; }
}

/// <summary>
/// The chart engine's host-neutral render entry point, for hosts that draw
/// whole images or tile sets rather than a live map: <c>s100 render</c>,
/// <c>s100 tiles</c>, <c>s100 mcp serve</c> and the library's PNG renderer.
/// Portrayal and raster run as jobs on the work scheduler (by default
/// <see cref="WorkScheduler.Shared"/>), in the <see cref="WorkClass.Visible"/>
/// class, since a caller is waiting on every one of them.
/// </summary>
/// <remarks>
/// This is the seam a chart session takes over once it exists (map rendering
/// slice&#160;8): the calls keep their shape, and the session supplies the
/// scheduler, caches and options.
/// </remarks>
public static class ChartRenderer
{
    /// <summary>
    /// Portrays every dataset in parallel, one <see cref="WorkStage.Portray"/>
    /// job each, and returns their composite inputs in request order.
    /// </summary>
    /// <param name="requests">The datasets, bottom-most first.</param>
    /// <param name="scheduler">The scheduler to run on; <see langword="null"/> for <see cref="WorkScheduler.Shared"/>.</param>
    /// <param name="cancellationToken">Cancels the outstanding portrayals.</param>
    /// <returns>The composite inputs, in request order.</returns>
    /// <exception cref="NotSupportedException">A processor is neither a vector nor a coverage portrayal source.</exception>
    public static async Task<IReadOnlyList<ChartCompositeInput>> PortrayAsync(
        IReadOnlyList<ChartPortrayalRequest> requests,
        IWorkScheduler? scheduler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Processor is not (IVectorPortrayalSource or ICoveragePortrayalSource))
            {
                throw new NotSupportedException(
                    $"Chart rendering is not supported for {request.Processor.Spec.Name}: "
                    + "the processor implements neither IVectorPortrayalSource nor ICoveragePortrayalSource.");
            }
        }

        scheduler ??= WorkScheduler.Shared;
        var started = Stopwatch.GetTimestamp();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var portrayals = new Task<ChartCompositeInput>[requests.Count];
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            portrayals[i] = scheduler.RunAsync(
                WorkStage.Portray,
                token => PortrayOneAsync(request, token),
                cancel.Token,
                WorkPriority.Visible(i));
        }

        try
        {
            var inputs = await Task.WhenAll(portrayals).ConfigureAwait(false);
            ChartRenderTelemetry.PortrayDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return inputs;
        }
        catch
        {
            // One failure ends the render; stop the portrayals still queued.
            await cancel.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Portrays the datasets in parallel (<see cref="PortrayAsync"/>), composites
    /// them, and renders the explicit or fitted viewport at
    /// <see cref="ChartCompositeOptions.DeviceScale"/>.
    /// </summary>
    /// <param name="requests">The datasets, bottom-most first.</param>
    /// <param name="compositor">The compositor.</param>
    /// <param name="options">Composite and render options.</param>
    /// <param name="scheduler">The scheduler to run on; <see langword="null"/> for <see cref="WorkScheduler.Shared"/>.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public static async Task<SKBitmap> RenderAsync(
        IReadOnlyList<ChartPortrayalRequest> requests,
        ChartCompositor compositor,
        ChartCompositeOptions options,
        IWorkScheduler? scheduler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(options);

        var inputs = await PortrayAsync(requests, scheduler, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Composite and raster on the scheduler too, behind nothing visible.
        return await (scheduler ?? WorkScheduler.Shared).RunAsync(
            WorkStage.Raster,
            _ => compositor.Render(inputs, options),
            cancellationToken,
            WorkPriority.Visible()).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders tile blocks as <see cref="WorkStage.Raster"/> jobs in the
    /// <see cref="WorkClass.Visible"/> class, queued in list order, and hands
    /// each block's result to <paramref name="write"/> in that same order.
    /// </summary>
    /// <remarks>
    /// <paramref name="finish"/> runs on the job, in parallel, so it is the
    /// place for per-tile work such as encoding; the tiles are disposed when it
    /// returns. <paramref name="write"/> runs on the caller's flow, one block at
    /// a time. At most <see cref="ChartTileJobOptions.MaxInFlight"/> blocks are
    /// rendered or waiting to be written at once.
    /// </remarks>
    /// <typeparam name="T">What <paramref name="finish"/> makes of a block's tiles.</typeparam>
    /// <param name="renderer">The tile renderer for the blocks' zoom level.</param>
    /// <param name="blocks">The blocks, in the order they are to be written.</param>
    /// <param name="finish">Turns a block's rendered tiles into what is written; runs on the job.</param>
    /// <param name="write">Writes one block's result.</param>
    /// <param name="options">Job settings; defaults when <see langword="null"/>.</param>
    /// <param name="scheduler">The scheduler to run on; <see langword="null"/> for <see cref="WorkScheduler.Shared"/>.</param>
    /// <param name="cancellationToken">Cancels the blocks not yet written.</param>
    /// <returns>A task that completes once every block is written.</returns>
    public static async Task RenderTilesAsync<T>(
        ChartTileRenderer renderer,
        IReadOnlyList<TileBlock> blocks,
        Func<IReadOnlyList<ChartTile>, T> finish,
        Func<T, ValueTask> write,
        ChartTileJobOptions? options = null,
        IWorkScheduler? scheduler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(finish);
        ArgumentNullException.ThrowIfNull(write);
        options ??= new ChartTileJobOptions();
        scheduler ??= WorkScheduler.Shared;

        int maxInFlight = options.MaxInFlight
            ?? 2 * Math.Max(1, scheduler.GetStats().Workers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInFlight, nameof(options));

        // Jobs are keyed on this call, so blocks of another call never coalesce with them.
        var owner = new object();
        var pending = new Queue<WorkInterest<T>>(maxInFlight);
        int next = 0;
        try
        {
            while (next < blocks.Count || pending.Count > 0)
            {
                while (next < blocks.Count && pending.Count < maxInFlight)
                {
                    var block = blocks[next];
                    pending.Enqueue(scheduler.Schedule(
                        new WorkKey(owner, block),
                        WorkStage.Raster,
                        WorkPriority.Visible(options.UrgencyOffset + next),
                        context => ValueTask.FromResult(RenderBlock(renderer, block, finish, context.Cancelled))));
                    next++;
                }

                using var head = pending.Dequeue();
                T result;
                using (cancellationToken.Register(static i => ((WorkInterest)i!).Dispose(), head))
                    result = await head.Result.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await write(result).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            // Releasing a queued job's only interest cancels it.
            while (pending.Count > 0)
                pending.Dequeue().Dispose();
        }
    }

    private static T RenderBlock<T>(
        ChartTileRenderer renderer, TileBlock block, Func<IReadOnlyList<ChartTile>, T> finish, CancellationToken cancelled)
    {
        cancelled.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var tiles = renderer.Render(block);
        try
        {
            return finish(tiles);
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
            ChartRenderTelemetry.TileBlockDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static async Task<ChartCompositeInput> PortrayOneAsync(ChartPortrayalRequest request, CancellationToken cancellationToken)
    {
        switch (request.Processor)
        {
            case IVectorPortrayalSource vector:
                {
                    var result = await vector.BuildVectorPortrayalAsync(request.Context, cancellationToken).ConfigureAwait(false);
                    return ChartCompositeInput.ForVector(result, request.Active);
                }

            default:
                {
                    var coverage = (ICoveragePortrayalSource)request.Processor;
                    var result = await coverage.BuildCoveragePortrayalAsync(request.Context, cancellationToken).ConfigureAwait(false);
                    return ChartCompositeInput.ForCoverage(result, request.Active);
                }
        }
    }
}
