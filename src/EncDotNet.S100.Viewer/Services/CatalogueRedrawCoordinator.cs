using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Redraws the loaded datasets of a product when its catalogues change
/// (design note <c>catalogue-panels.md</c> D6, audit A5): on
/// <see cref="CatalogueRegistry.CataloguesChanged"/> it asks the loader to
/// open those datasets again, so they get processors built with the new
/// catalogues. Datasets of other products aren't touched.
/// </summary>
/// <remarks>
/// A dataset belongs to a product when its product spec or its portrayal
/// spec is that product, so an S-57 cell (drawn with the S-101 catalogues)
/// is redrawn when an S-101 catalogue changes.
/// </remarks>
internal sealed class CatalogueRedrawCoordinator : IDisposable
{
    private readonly CatalogueRegistry _registry;
    private readonly Func<IEnumerable<DatasetEntry>> _entries;
    private readonly IDatasetLoaderService _loader;
    private readonly ILogger _logger;

    /// <summary>Starts redrawing on every change <paramref name="registry"/> raises.</summary>
    /// <param name="registry">The catalogue registry.</param>
    /// <param name="entries">The datasets in the panel.</param>
    /// <param name="loader">Opens them again.</param>
    /// <param name="logger">Logs a failed redraw.</param>
    public CatalogueRedrawCoordinator(
        CatalogueRegistry registry,
        Func<IEnumerable<DatasetEntry>> entries,
        IDatasetLoaderService loader,
        ILogger<CatalogueRedrawCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(loader);
        _registry = registry;
        _entries = entries;
        _loader = loader;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _registry.CataloguesChanged += OnCataloguesChanged;
    }

    /// <summary>The task of the latest redraw, for tests and callers that wait on it.</summary>
    public Task LastRedraw { get; private set; } = Task.CompletedTask;

    /// <summary>The loaded datasets a change to <paramref name="spec"/>'s catalogues redraws.</summary>
    /// <param name="spec">The product, e.g. <c>"S-101"</c>.</param>
    public IReadOnlyList<DatasetEntry> AffectedBy(string spec)
    {
        if (!SpecName.TryNormalize(spec, out var name))
            return [];
        return [.. _entries().Where(e =>
            e.IsLoaded
            && !e.IsSource
            && (Matches(e.ProductSpec, name) || Matches(e.PortrayalSpec, name)))];
    }

    /// <summary>Redraws the loaded datasets of <paramref name="spec"/>.</summary>
    /// <returns>How many datasets were redrawn.</returns>
    public async Task<int> RedrawAsync(string spec)
    {
        var affected = AffectedBy(spec);
        if (affected.Count == 0)
            return 0;
        _logger.LogInformation(
            "Redrawing {Count} {Spec} dataset(s) with the new catalogues", affected.Count, spec);
        await _loader.ReloadEntriesAsync(affected).ConfigureAwait(true);
        return affected.Count;
    }

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e)
    {
        LastRedraw = RedrawLoggingFailuresAsync(e.Spec.Name);
    }

    private async Task RedrawLoggingFailuresAsync(string spec)
    {
        try
        {
            await RedrawAsync(spec).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not redraw the {Spec} datasets after a catalogue change", spec);
        }
    }

    private static bool Matches(string? spec, string name) =>
        SpecName.TryNormalize(spec, out var normalized) && normalized == name;

    /// <inheritdoc />
    public void Dispose() => _registry.CataloguesChanged -= OnCataloguesChanged;
}
