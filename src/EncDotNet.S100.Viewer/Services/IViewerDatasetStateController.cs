using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Reads and changes a loaded dataset's display state for agents (MCP
/// <c>set_dataset_state</c>, #715): the same properties the Datasets list's
/// eye icon and opacity control set.
/// </summary>
internal interface IViewerDatasetStateController
{
    /// <summary>
    /// Applies <paramref name="visible"/> and <paramref name="opacity"/> (each
    /// left unchanged when null) to the dataset with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The dataset id, as <c>list_datasets</c> reports it.</param>
    /// <param name="visible">Whether the dataset draws, or null to leave it.</param>
    /// <param name="opacity">Opacity in 0..1, or null to leave it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The state before and after, or null when no dataset has that id.</returns>
    Task<DatasetStateOutcome?> SetStateAsync(string id, bool? visible, double? opacity, CancellationToken ct = default);
}

/// <summary>A dataset's display state before and after a change.</summary>
/// <param name="Id">The dataset id.</param>
/// <param name="Spec">The product specification.</param>
/// <param name="Visible">Whether the dataset draws, afterwards.</param>
/// <param name="Opacity">The dataset's opacity, afterwards.</param>
/// <param name="PreviousVisible">Whether the dataset drew, before.</param>
/// <param name="PreviousOpacity">The dataset's opacity, before.</param>
internal sealed record DatasetStateOutcome(
    string Id,
    string Spec,
    bool Visible,
    double Opacity,
    bool PreviousVisible,
    double PreviousOpacity);

/// <summary>Default <see cref="IViewerDatasetStateController"/> over <see cref="DatasetsViewModel"/>.</summary>
internal sealed class ViewerDatasetStateController : IViewerDatasetStateController
{
    private readonly DatasetsViewModel _datasets;
    private readonly Func<Action, Task> _dispatch;

    public ViewerDatasetStateController(DatasetsViewModel datasets, Func<Action, Task>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        _datasets = datasets;
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
    }

    /// <inheritdoc />
    public async Task<DatasetStateOutcome?> SetStateAsync(string id, bool? visible, double? opacity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ct.ThrowIfCancellationRequested();
        DatasetStateOutcome? outcome = null;
        await _dispatch(() =>
        {
            var entry = _datasets.Entries.FirstOrDefault(e => string.Equals(e.DisplayName, id, StringComparison.Ordinal))
                ?? _datasets.Entries.FirstOrDefault(e => string.Equals(e.Id.Value, id, StringComparison.Ordinal));
            if (entry is null)
                return;

            var previousVisible = entry.IsVisible;
            var previousOpacity = entry.Opacity;
            if (visible is { } show)
                entry.IsVisible = show;
            if (opacity is { } alpha)
                entry.Opacity = alpha;
            outcome = new DatasetStateOutcome(
                entry.DisplayName, entry.ProductSpec, entry.IsVisible, entry.Opacity, previousVisible, previousOpacity);
        }).ConfigureAwait(false);
        return outcome;
    }
}
