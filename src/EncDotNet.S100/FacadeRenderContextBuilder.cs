using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100;

/// <summary>
/// Maps facade <see cref="S100RendererOptions"/> onto the
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

        return new RenderContext
        {
            Palette = palette,
            SymbolScale = symbolScale,
            TextScale = textScale,
            HiddenInstructionCategories = hidden,
            TimeStep = timeStep,
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
}
