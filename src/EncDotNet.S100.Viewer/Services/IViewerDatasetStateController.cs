using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Reads and changes a loaded dataset's display state for agents (MCP
/// <c>set_dataset_state</c>, #715): the same properties the Datasets list's
/// eye icon and opacity control set; and selects a dataset in the Datasets
/// panel with its inspector tab (MCP <c>select_dataset</c>).
/// </summary>
internal interface IViewerDatasetStateController
{
    /// <summary>
    /// Applies <paramref name="visible"/>, <paramref name="opacity"/> and
    /// <paramref name="order"/> (each left unchanged when null) to the dataset
    /// with <paramref name="id"/>. With <paramref name="map"/>, the visibility
    /// applies to that map only; without it, to every map (split map step 2
    /// §2.7, Q6). Opacity and order are global.
    /// </summary>
    /// <param name="id">The dataset id, as <c>list_datasets</c> reports it.</param>
    /// <param name="map">The map whose visibility to set, as <c>list_maps</c> reports it, or null for every map.</param>
    /// <param name="visible">Whether the dataset draws, or null to leave it.</param>
    /// <param name="opacity">Opacity in 0..1, or null to leave it.</param>
    /// <param name="order">A move in the draw order, as the Datasets list's move buttons and menu do, or null to leave it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The state before and after; <see cref="DatasetNotFound"/> when no
    /// dataset has that id, <see cref="MapNotFound"/> when no map does.
    /// </returns>
    Task<ToolResult<DatasetStateOutcome>> SetStateAsync(
        string id, string? map, bool? visible, double? opacity, DatasetOrderMove? order, CancellationToken ct = default);

    /// <summary>
    /// Selects the dataset with <paramref name="id"/> in the Datasets panel as
    /// a click on its row does (switching the panel to its Datasets tab), and
    /// optionally switches the inspector to <paramref name="tab"/>. When the
    /// dataset is still loading, waits up to <paramref name="validationTimeout"/>
    /// for its validation to finish before reporting.
    /// </summary>
    /// <param name="id">The dataset id, as <c>list_datasets</c> reports it.</param>
    /// <param name="tab">The inspector tab to show, or null to leave it.</param>
    /// <param name="validationTimeout">How long to wait for a loading dataset's validation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The selection and its validation summary, or null when no dataset has that id.</returns>
    Task<DatasetSelectionOutcome?> SelectAsync(string id, DatasetInspectorTab? tab, TimeSpan validationTimeout, CancellationToken ct = default);
}

/// <summary>A move in the Datasets list's draw order: the top row paints over the rest.</summary>
internal enum DatasetOrderMove
{
    /// <summary>One row up (Move up).</summary>
    Up,

    /// <summary>One row down (Move down).</summary>
    Down,

    /// <summary>To the top row (Bring to front).</summary>
    Front,

    /// <summary>To the bottom row (Send to back).</summary>
    Back,
}

/// <summary>Whether a dataset's validation has run.</summary>
internal enum DatasetValidationState
{
    /// <summary>The spec's rule pack ran; the counts are its findings.</summary>
    Ready,

    /// <summary>The dataset loaded but its spec has no rule pack.</summary>
    NoRulePack,

    /// <summary>The dataset has not loaded (still loading, deferred until it is in view, or failed).</summary>
    NotLoaded,
}

/// <summary>A dataset selected in the Datasets panel, with its validation summary.</summary>
/// <param name="Id">The dataset id.</param>
/// <param name="Spec">The product specification.</param>
/// <param name="PreviousId">The dataset the inspector showed before, or null.</param>
/// <param name="Tab">The inspector tab shown, afterwards.</param>
/// <param name="Deferred">True for an exchange-set cell that loads only once it is in view.</param>
/// <param name="ValidationState">Whether validation has run.</param>
/// <param name="Errors">Error findings.</param>
/// <param name="Warnings">Warning findings.</param>
/// <param name="Infos">Info findings.</param>
/// <param name="Located">Findings with a location, drawn by the map's validation overlay.</param>
/// <param name="Message">The Validation tab's heading: its counts summary, or its empty-state message.</param>
internal sealed record DatasetSelectionOutcome(
    string Id,
    string Spec,
    string? PreviousId,
    DatasetInspectorTab Tab,
    bool Deferred,
    DatasetValidationState ValidationState,
    int Errors,
    int Warnings,
    int Infos,
    int Located,
    string? Message);

/// <summary>A dataset's display state before and after a change.</summary>
/// <param name="Id">The dataset id.</param>
/// <param name="Spec">The product specification.</param>
/// <param name="Visible">Whether the dataset is visible afterwards: in the map asked about, else in any map.</param>
/// <param name="Opacity">The dataset's opacity, afterwards.</param>
/// <param name="PreviousVisible">Whether the dataset was visible before, as <paramref name="Visible"/> asks.</param>
/// <param name="PreviousOpacity">The dataset's opacity, before.</param>
/// <param name="Position">The dataset's row in the draw order, afterwards (0 paints on top).</param>
/// <param name="PreviousPosition">The dataset's row in the draw order, before.</param>
/// <param name="ViewingGroups">The dataset's own viewing-group filter, or <c>null</c>.</param>
/// <param name="ShownIn">The ids of the maps that draw it afterwards, in reading order (split map step 2 §2.7).</param>
internal sealed record DatasetStateOutcome(
    string Id,
    string Spec,
    bool Visible,
    double Opacity,
    bool PreviousVisible,
    double PreviousOpacity,
    int Position,
    int PreviousPosition,
    ViewingGroupFilter? ViewingGroups,
    IReadOnlyList<string> ShownIn);

/// <summary>Default <see cref="IViewerDatasetStateController"/> over <see cref="DatasetsViewModel"/>.</summary>
internal sealed class ViewerDatasetStateController : IViewerDatasetStateController
{
    private readonly DatasetsViewModel _datasets;
    private readonly ICapabilityAccessor<MapSplitController> _maps;
    private readonly Func<Action, Task> _dispatch;

    /// <summary>Creates the controller.</summary>
    /// <param name="datasets">The Datasets list.</param>
    /// <param name="maps">The window's maps, once it has built them: resolves a map id and reports where a dataset is drawn.</param>
    /// <param name="dispatch">Runs an action on the UI thread.</param>
    public ViewerDatasetStateController(DatasetsViewModel datasets, ICapabilityAccessor<MapSplitController> maps, Func<Action, Task> dispatch)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(dispatch);
        _datasets = datasets;
        _maps = maps;
        _dispatch = dispatch;
    }

    /// <inheritdoc />
    public async Task<ToolResult<DatasetStateOutcome>> SetStateAsync(
        string id, string? map, bool? visible, double? opacity, DatasetOrderMove? order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ct.ThrowIfCancellationRequested();
        ToolResult<DatasetStateOutcome>? outcome = null;
        await _dispatch(() =>
        {
            var entry = Find(id);
            if (entry is null)
            {
                outcome = ToolResult<DatasetStateOutcome>.Err(new DatasetNotFound(new DatasetId(id)));
                return;
            }

            var maps = _maps.Current;
            MapViewId? target = null;
            if (map is not null)
            {
                target = maps?.FindMap(map);
                if (target is null)
                {
                    outcome = ToolResult<DatasetStateOutcome>.Err(maps?.NotFound(map) ?? new MapNotFound(map, []));
                    return;
                }
            }

            bool VisibleNow() => target is { } one ? entry.IsVisibleIn(one) : entry.IsVisibleAnywhere;

            var previousVisible = VisibleNow();
            var previousOpacity = entry.Opacity;
            var previousPosition = _datasets.Entries.IndexOf(entry);
            // With a map, visibility applies to that map; without one, to
            // every map (split map step 2 §2.7, Q6).
            if (visible is { } show)
            {
                if (target is { } one)
                    entry.SetVisibleIn(one, show);
                else
                    entry.SetVisible(show);
            }
            if (opacity is { } alpha)
                entry.Opacity = alpha;
            var move = order switch
            {
                DatasetOrderMove.Up => _datasets.MoveUpCommand,
                DatasetOrderMove.Down => _datasets.MoveDownCommand,
                DatasetOrderMove.Front => _datasets.BringToFrontCommand,
                DatasetOrderMove.Back => _datasets.SendToBackCommand,
                _ => null,
            };
            move?.Execute(entry);
            outcome = ToolResult<DatasetStateOutcome>.Ok(new DatasetStateOutcome(
                entry.DisplayName, entry.ProductSpec, VisibleNow(), entry.Opacity, previousVisible, previousOpacity,
                _datasets.Entries.IndexOf(entry), previousPosition, entry.ViewingGroups,
                maps?.ShownIn(entry) ?? []));
        }).ConfigureAwait(false);
        return outcome ?? throw new InvalidOperationException("The dispatcher didn't run the change.");
    }

    /// <inheritdoc />
    public async Task<DatasetSelectionOutcome?> SelectAsync(
        string id, DatasetInspectorTab? tab, TimeSpan validationTimeout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ct.ThrowIfCancellationRequested();
        DatasetEntry? entry = null;
        string? previousId = null;
        var validated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            // The loader sets IsLoaded and then the validation report; an
            // unload clears IsLoaded first, so only a report on a loaded
            // entry means validation finished.
            if (e.PropertyName == nameof(DatasetEntry.Validation) && entry!.IsLoaded)
                validated.TrySetResult();
        };
        var waiting = false;

        await _dispatch(() =>
        {
            entry = Find(id);
            if (entry is null)
                return;
            previousId = _datasets.SelectedEntry?.DisplayName;
            _datasets.SelectDataset(entry);
            if (tab is { } wanted)
                _datasets.InspectorTab = wanted;
            if (!entry.IsLoaded && !entry.IsDeferred && validationTimeout > TimeSpan.Zero)
            {
                entry.PropertyChanged += onChanged;
                waiting = true;
            }
        }).ConfigureAwait(false);
        if (entry is null)
            return null;

        if (waiting)
        {
            try
            {
                await validated.Task.WaitAsync(validationTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Report the dataset as not loaded yet.
            }
            finally
            {
                await _dispatch(() => entry.PropertyChanged -= onChanged).ConfigureAwait(false);
            }
        }

        DatasetSelectionOutcome? outcome = null;
        await _dispatch(() => outcome = Summarise(entry, previousId, _datasets.InspectorTab)).ConfigureAwait(false);
        return outcome;
    }

    private DatasetEntry? Find(string id) =>
        _datasets.Entries.FirstOrDefault(e => string.Equals(e.DisplayName, id, StringComparison.Ordinal))
        ?? _datasets.Entries.FirstOrDefault(e => string.Equals(e.Id.Value, id, StringComparison.Ordinal));

    private static DatasetSelectionOutcome Summarise(DatasetEntry entry, string? previousId, DatasetInspectorTab tab)
    {
        var state = entry.HasValidationRulePack
            ? DatasetValidationState.Ready
            : entry.IsLoaded ? DatasetValidationState.NoRulePack : DatasetValidationState.NotLoaded;
        var message = state switch
        {
            DatasetValidationState.NotLoaded => null,
            _ when entry.HasValidationFindings => entry.ValidationCountsSummary,
            _ => entry.ValidationEmptyStateMessage,
        };
        return new DatasetSelectionOutcome(
            entry.DisplayName,
            entry.ProductSpec,
            previousId,
            tab,
            entry.IsDeferred,
            state,
            entry.ValidationErrorCount,
            entry.ValidationWarningCount,
            entry.ValidationInfoCount,
            entry.Findings.Count(f => f.HasSpatialLocation),
            message);
    }
}
