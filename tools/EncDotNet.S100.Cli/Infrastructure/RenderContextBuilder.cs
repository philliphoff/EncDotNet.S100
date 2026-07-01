using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds the spec-specific <see cref="RenderContext"/> the dataset processors
/// expect, mapping CLI options (palette, scales, time-step index, hidden
/// instruction categories) onto the correct context record. Time-series specs
/// resolve their <c>--time-step</c> index against
/// <see cref="ITimeAwareDatasetProcessor"/>.
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
        BasemapKind basemap = BasemapKind.None)
    {
        DateTime? timeStep = ResolveTimeStep(processor, timeStepIndex);

        RenderContext context = processor.Spec.Name switch
        {
            "S-101" => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hiddenCategories },
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

        return context with { Basemap = basemap };
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
