using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100;

/// <summary>
/// Maps facade <see cref="S100RendererOptions"/> onto the spec-specific
/// <see cref="RenderContext"/> the dataset processors expect. Mirrors the
/// mapping the <c>s100</c> CLI performs so the facade drives identical pipelines.
/// </summary>
internal static class FacadeRenderContextBuilder
{
    public static RenderContext Build(IDatasetProcessor processor, S100RendererOptions options)
    {
        var palette = options.Palette;
        var symbolScale = options.SymbolScale;
        var textScale = options.TextScale;
        var hidden = options.HiddenCategories;
        DateTime? timeStep = ResolveTimeStep(processor, options.TimeStep);

        RenderContext context = processor.Spec.Name switch
        {
            "S-101" => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-102" => new S102RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-104" => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-111" => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-122" => new S122RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-124" => new S124RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-125" => new S125RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-127" => new S127RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-129" => new S129RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-201" => new S201RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            "S-411" => new S411RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
            _ => new GenericRenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, HiddenInstructionCategories = hidden },
        };

        return context with
        {
            Basemap = options.Basemap,
            EcdisDisplay = options.EcdisDisplay,
            DisplayModeId = ResolveDisplayModeId(processor, options),
        };
    }

    /// <summary>
    /// Resolves the effective S-100 Part 9 §11.7 display-mode id, mirroring
    /// <see cref="MapPresentationState.ApplyTo"/>: a per-spec entry in
    /// <see cref="EcdisDisplaySettings.ActiveDisplayModes"/> (keyed on the
    /// processor's <see cref="IDatasetProcessor.PortrayalSpec"/>, so a translated
    /// S-57→S-101 cell resolves under <c>"S-101"</c>) wins over the global
    /// <see cref="S100RendererOptions.DisplayModeId"/>. A <c>null</c>/empty entry
    /// leaves the global id in place.
    /// </summary>
    private static string? ResolveDisplayModeId(IDatasetProcessor processor, S100RendererOptions options)
    {
        var perSpec = options.EcdisDisplay?.ActiveDisplayModes.GetValueOrDefault(
            processor.PortrayalSpec.Name);
        return string.IsNullOrEmpty(perSpec) ? options.DisplayModeId : perSpec;
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
    /// Concrete fallback for specs without their own context record (e.g. S-128,
    /// S-131, S-421), which consume only the shared <see cref="RenderContext"/>
    /// properties.
    /// </summary>
    private sealed record GenericRenderContext : RenderContext;
}
