using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Builds the current render context for a processor and selected dataset time.
/// </summary>
/// <param name="processor">
/// The leased processor. The callback must not retain, dispose, or re-acquire
/// it.
/// </param>
/// <param name="selectedTime">
/// The dataset sample selected by the map session, or <see langword="null"/> for
/// a non-time-aware dataset.
/// </param>
/// <returns>The context to use for the render.</returns>
public delegate RenderContext? MapsuiRenderContextFactory(
    IDatasetProcessor processor,
    DateTime? selectedTime);
