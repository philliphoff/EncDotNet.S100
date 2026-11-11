using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds the <see cref="RenderContext"/> the dataset processors expect,
/// mapping CLI options (palette, scales, time-step index, hidden instruction
/// categories) onto it. Time-series specs
/// resolve their <c>--time-step</c> index, or the step nearest an explicit
/// instant, against <see cref="ITimeAwareDatasetProcessor"/>.
/// </summary>
internal static class RenderContextBuilder
{
    public static RenderContext Build(
        IDatasetProcessor processor,
        PaletteType palette,
        double symbolScale,
        double textScale,
        int timeStepIndex,
        DrawingInstructionCategory hiddenCategories = DrawingInstructionCategory.None,
        BasemapKind basemap = BasemapKind.None,
        string? displayModeId = null,
        Viewport? viewport = null,
        DateTime? instant = null)
    {
        DateTime? timeStep = instant is { } at
            ? NearestTimeStep(processor, at)
            : ResolveTimeStep(processor, timeStepIndex);

        return new RenderContext
        {
            Palette = palette,
            SymbolScale = symbolScale,
            TextScale = textScale,
            HiddenInstructionCategories = hiddenCategories,
            TimeStep = timeStep,
            Basemap = basemap,
            DisplayModeId = displayModeId,
            Viewport = viewport,
        };
    }

    /// <summary>
    /// The time step of <paramref name="processor"/> nearest <paramref name="instant"/>
    /// (UTC), or <see langword="null"/> when it has none.
    /// </summary>
    internal static DateTime? NearestTimeStep(IDatasetProcessor processor, DateTime instant)
    {
        if (processor is not ITimeAwareDatasetProcessor timeAware || timeAware.AvailableTimes.Count == 0)
            return null;

        return Nearest(timeAware.AvailableTimes, instant);
    }

    /// <summary>The entry of <paramref name="times"/> nearest <paramref name="instant"/>; the earlier one on a tie.</summary>
    internal static DateTime Nearest(IReadOnlyList<DateTime> times, DateTime instant)
    {
        var best = times[0];
        foreach (var time in times)
        {
            if (Math.Abs((time - instant).Ticks) < Math.Abs((best - instant).Ticks))
                best = time;
        }

        return best;
    }

    private static DateTime? ResolveTimeStep(IDatasetProcessor processor, int timeStepIndex)
    {
        if (processor is not ITimeAwareDatasetProcessor timeAware)
            return null;

        var times = timeAware.AvailableTimes;
        if (times.Count == 0)
            return null;

        int idx = Math.Clamp(timeStepIndex, 0, times.Count - 1);
        return times[idx];
    }
}
