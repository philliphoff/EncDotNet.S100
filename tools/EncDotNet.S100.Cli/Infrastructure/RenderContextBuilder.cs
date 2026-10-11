using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds the spec-specific <see cref="RenderContext"/> the dataset processors
/// expect, mapping CLI options (palette, scales, time-step index, hidden
/// instruction categories) onto the correct context record. Time-series specs
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

        RenderContext context = processor.Spec.Name switch
        {
            "S-101" => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            // S-401 inland ENC renders through the S-101 pipeline with its own
            // catalogues, so it takes the same render context.
            "S-401" => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-102" => new S102RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-104" => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-111" => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-122" => new S122RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-124" => new S124RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-125" => new S125RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-127" => new S127RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-129" => new S129RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-201" => new S201RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            "S-411" => new S411RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
            _ => new GenericRenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
        };

        return context with { Basemap = basemap, DisplayModeId = displayModeId, Viewport = viewport };
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

    /// <summary>
    /// Concrete fallback for specs without their own context record (e.g. S-421,
    /// S-128, S-131), which consume only the shared <see cref="RenderContext"/>
    /// properties.
    /// </summary>
    private sealed record GenericRenderContext : RenderContext;
}
