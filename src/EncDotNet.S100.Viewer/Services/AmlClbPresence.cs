using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Whether AML Contour Line Bathymetry (CLB) is drawn on the map: loaded,
/// shown and drawn by the AML portrayal.
/// </summary>
/// <remarks>
/// <para>
/// While it is, the ENC safety contour gains a halo so it stays the
/// strongest line under CLB's many contours (AML design handoff decision 22):
/// <see cref="MapPresentationStateProjection"/> puts it in every presentation
/// snapshot, and <see cref="ViewerPresentationCoordinator"/> re-draws the
/// depth layers when it changes. The map also shows
/// the corner chip "AML · CLB not used for depth checks" (decision 23,
/// <c>ClbStyles.NotForDepthChecksChip</c>). The chip is viewer chrome for
/// slice S6 (#1374): bind its visibility to <see cref="IsClbDrawn"/> and
/// refresh it on <see cref="Changed"/>.
/// </para>
/// <para>
/// It says nothing about depths: AML is never a source of depth answers or
/// route checks, drawn or not.
/// </para>
/// </remarks>
internal interface IAmlClbPresence
{
    /// <summary>Whether any CLB cell is loaded, shown and drawn.</summary>
    bool IsClbDrawn { get; }

    /// <summary>Raised on the UI thread when <see cref="IsClbDrawn"/> changes.</summary>
    event Action? Changed;
}

/// <summary>
/// <see cref="IAmlClbPresence"/> over the loaded datasets: recomputed when a
/// dataset loads, is removed, or is shown, hidden or unloaded.
/// </summary>
/// <remarks>
/// It is attached to the loader after the container is built
/// (<see cref="Attach"/>): the presentation projection reads it, and the
/// loader is built from the projection's first snapshot.
/// </remarks>
internal sealed class AmlClbPresence : IAmlClbPresence, IDisposable
{
    private readonly HashSet<DatasetEntry> _watched = [];
    private IDatasetLoaderService? _loader;
    private bool _isClbDrawn;

    /// <inheritdoc />
    public bool IsClbDrawn => _isClbDrawn;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Whether <paramref name="processor"/> draws a CLB cell.</summary>
    /// <param name="processor">A loaded dataset's processor.</param>
    public static bool DrawsClb(IDatasetProcessor processor) =>
        processor is AmlDatasetProcessor { Drawn: true } aml
        && string.Equals(aml.Info.ProductCode, "CLB", StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts following <paramref name="loader"/>'s datasets.</summary>
    /// <param name="loader">The loader.</param>
    public void Attach(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        if (_loader is not null)
            throw new InvalidOperationException("Already attached to a dataset loader.");
        _loader = loader;
        _loader.DatasetLoaded += OnDatasetLoaded;
        _loader.DatasetRemoved += OnDatasetRemoved;
    }

    public void Dispose()
    {
        if (_loader is not null)
        {
            _loader.DatasetLoaded -= OnDatasetLoaded;
            _loader.DatasetRemoved -= OnDatasetRemoved;
        }

        foreach (var entry in _watched)
            entry.PropertyChanged -= OnEntryChanged;
        _watched.Clear();
    }

    private void OnDatasetLoaded(DatasetEntry entry)
    {
        if (_watched.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
        Update();
    }

    private void OnDatasetRemoved(DatasetEntry entry)
    {
        if (_watched.Remove(entry))
            entry.PropertyChanged -= OnEntryChanged;
        Update();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.IsVisible) or nameof(DatasetEntry.IsLoaded))
            Update();
    }

    private void Update()
    {
        if (_loader is null)
            return;

        bool drawn = false;
        using (var processors = _loader.AcquireProcessors())
        {
            foreach (var (entry, processor) in processors)
            {
                if (entry.IsVisible && entry.IsLoaded && DrawsClb(processor))
                {
                    drawn = true;
                    break;
                }
            }
        }
        if (drawn == _isClbDrawn)
            return;
        _isClbDrawn = drawn;
        Changed?.Invoke();
    }
}
