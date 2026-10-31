using System.Collections.Specialized;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>A dataset whose Active flag a combination changed.</summary>
/// <param name="Id">The dataset id.</param>
/// <param name="Product">Its product key.</param>
/// <param name="Active">Its Active flag after the change.</param>
internal sealed record DisplayCombinationDatasetChange(string Id, string Product, bool Active);

/// <summary>What applying a display combination did.</summary>
/// <param name="PreviousId">The combination active before, or <see langword="null"/> for none.</param>
/// <param name="ActiveId">The combination active after.</param>
/// <param name="Datasets">The datasets whose Active flag changed.</param>
/// <param name="NotLoaded">Products the combination names that aren't loaded; nothing is fetched for them.</param>
internal sealed record DisplayCombinationApplyResult(
    string? PreviousId,
    string ActiveId,
    IReadOnlyList<DisplayCombinationDatasetChange> Datasets,
    IReadOnlyList<string> NotLoaded);

/// <summary>
/// Owns the display combinations (built-ins and the mariner's own), the active
/// one and its modified marker (design note
/// <c>docs/design/predefined-display-combinations.md</c>). Applying a
/// combination sets the ECDIS display state it covers and makes the datasets
/// of the products it leaves out inactive, through
/// <see cref="IDatasetLoaderService.SetActive"/>, so the S-98 rules stay
/// consistent with the picture. It never touches mariner settings, the
/// Navigational warnings switch, display planes, the palette, dynamic sources
/// or routes, and analysis (the route check, the depth probe) keeps using
/// every loaded dataset.
/// </summary>
internal sealed class DisplayCombinationService : IDisposable
{
    private readonly EcdisDisplayState _state;
    private readonly IDatasetLoaderService _loader;
    private readonly DatasetsViewModel _datasets;
    private readonly ViewerSettings _settings;
    private readonly IReadOnlyList<DisplayCombination> _builtIns;
    private readonly List<DisplayCombination> _user = [];
    private DisplayCombination? _active;
    private IReadOnlyList<DisplayCombinationChange> _changes = [];
    private int _applying;
    private readonly Action<Action>? _post;
    private bool _recomputePending;

    /// <summary>
    /// Creates the service, restoring the mariner's combinations and the
    /// active one from <paramref name="settings"/>. The active combination is
    /// not reapplied: the persisted display settings are the truth, and the
    /// modified marker is worked out against them. Products it leaves out
    /// load inactive from here on.
    /// </summary>
    /// <param name="state">The ECDIS display state.</param>
    /// <param name="loader">Sets the datasets' Active flags and runs the load policies.</param>
    /// <param name="datasets">The loaded datasets.</param>
    /// <param name="settings">Keeps the mariner's combinations and the active id.</param>
    /// <param name="post">
    /// Queues work on the UI thread, so a burst of layer-stack changes (an
    /// exchange set loading) works out the modified marker once; <see langword="null"/>
    /// works it out on every change.
    /// </param>
    public DisplayCombinationService(
        EcdisDisplayState state,
        IDatasetLoaderService loader,
        DatasetsViewModel datasets,
        ViewerSettings settings,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(settings);
        _state = state;
        _loader = loader;
        _datasets = datasets;
        _settings = settings;
        _post = post;
        _builtIns = BuiltInDisplayCombinations.Create();

        var ids = new HashSet<string>(_builtIns.Select(b => b.Id), StringComparer.Ordinal);
        foreach (var saved in settings.DisplayCombinations)
        {
            if (saved?.ToCombination() is { } combination && ids.Add(combination.Id))
                _user.Add(combination);
        }
        _active = settings.ActiveDisplayCombinationId is { } activeId ? Find(activeId) : null;

        RegisterLoadPolicies();
        _state.Changed += Recompute;
        _loader.ActiveChanged += OnActiveChanged;
        _loader.LayerStackChanged += RecomputeSoon;
        _loader.DatasetLoaded += OnDatasetLoaded;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        Recompute();
    }

    /// <summary>Raised after the list, the active combination or its modified marker changes.</summary>
    public event Action? Changed;

    /// <summary>Every combination: the built-ins, then the mariner's own.</summary>
    public IReadOnlyList<DisplayCombination> Combinations => [.. _builtIns, .. _user];

    /// <summary>The built-in combinations, in menu order.</summary>
    public IReadOnlyList<DisplayCombination> BuiltIns => _builtIns;

    /// <summary>The mariner's own combinations, in the order saved.</summary>
    public IReadOnlyList<DisplayCombination> UserCombinations => _user;

    /// <summary>The active combination, or <see langword="null"/> for <b>Custom</b>.</summary>
    public DisplayCombination? Active => _active;

    /// <summary>
    /// True when a control the active combination covers no longer matches it
    /// (<b>Pilotage (modified)</b>).
    /// </summary>
    public bool IsModified => _changes.Count > 0;

    /// <summary>The controls that differ from the active combination; empty when it isn't modified.</summary>
    public IReadOnlyList<DisplayCombinationChange> Changes => _changes;

    /// <summary>The product keys of the loaded datasets.</summary>
    public IReadOnlySet<string> LoadedProducts =>
        new HashSet<string>(
            _datasets.Entries.Select(DisplayCombinationProducts.KeyOf),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The combination with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public DisplayCombination? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Combinations.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// The products <paramref name="combination"/> names that aren't loaded,
    /// in display order. Empty for a combination of every product.
    /// </summary>
    public IReadOnlyList<string> NotLoaded(DisplayCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);
        var loaded = LoadedProducts;
        return [.. combination.Products.Where(p => !loaded.Contains(p)).Order(DisplayCombinationProducts.Order)];
    }

    /// <summary>
    /// Makes the combination with <paramref name="id"/> active and applies it;
    /// <see langword="null"/> when there is none.
    /// </summary>
    public DisplayCombinationApplyResult? Apply(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Find(id) is { } combination ? Apply(combination) : null;
    }

    /// <summary>
    /// Applies the active combination again, putting back every control it
    /// covers (<b>Reapply</b>); <see langword="null"/> when none is active.
    /// </summary>
    public DisplayCombinationApplyResult? Reapply() =>
        _active is { } active ? Apply(active) : null;

    private DisplayCombinationApplyResult Apply(DisplayCombination combination)
    {
        var previous = _active?.Id;
        var changed = new List<DisplayCombinationDatasetChange>();
        _applying++;
        try
        {
            _active = combination;
            RegisterLoadPolicies();

            _state.ApplyPicture(
                combination.Category,
                combination.HiddenViewingGroups,
                combination.ShownViewingGroups,
                combination.DisplayModes,
                combination.LayerVisibility);

            foreach (var entry in _datasets.Entries.ToArray())
            {
                var product = DisplayCombinationProducts.KeyOf(entry);
                var want = combination.Includes(product);
                var id = entry.Id.Value;
                if (_loader.GetActive(id) != want)
                {
                    _loader.SetActive(id, want);
                    if (_loader.GetActive(id) == want)
                        changed.Add(new(id, product, want));
                }
                // A registered cell that hasn't loaded yet: the load policy
                // decides it, but keep the row in step meanwhile.
                if (!entry.IsLoaded)
                    entry.IsActive = want;

                foreach (var subLayer in entry.SubLayers.ToArray())
                {
                    if (combination.SubLayerVisibility.TryGetValue(subLayer.Key, out var visible))
                        subLayer.IsVisible = visible;
                }
            }
        }
        finally
        {
            _applying--;
        }

        _settings.ActiveDisplayCombinationId = combination.Id;
        Save();
        Recompute(force: true);
        return new DisplayCombinationApplyResult(previous, combination.Id, changed, NotLoaded(combination));
    }

    /// <summary>
    /// Captures what the map shows as a combination: the products with an
    /// active dataset, the category, hidden viewing groups (and the S-101 text
    /// groups shown), display modes, optional-layer switches and the
    /// sub-layers every dataset agrees on.
    /// </summary>
    public DisplayCombination CaptureCurrent(string id, string label)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);
        var snapshot = Snapshot();
        var display = snapshot.Display;

        var products = new HashSet<string>(
            snapshot.Datasets.Where(d => d.Active).Select(d => d.Product),
            StringComparer.OrdinalIgnoreCase);

        var hidden = display.HiddenViewingGroups
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlySet<int>)new HashSet<int>(kv.Value), StringComparer.OrdinalIgnoreCase);
        var shown = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
        display.HiddenViewingGroups.TryGetValue(DisplayCombinationProducts.Chart, out var chartHidden);
        var textShown = BuiltInDisplayCombinations.S101ImportantText
            .Concat(BuiltInDisplayCombinations.S101OtherText)
            .Where(g => chartHidden?.Contains(g) != true)
            .ToHashSet();
        if (textShown.Count > 0)
            shown[DisplayCombinationProducts.Chart] = textShown;

        var subLayers = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var group in snapshot.Datasets
            .SelectMany(d => d.SubLayers)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var values = group.Select(kv => kv.Value).Distinct().ToArray();
            if (values.Length == 1)
                subLayers[group.Key] = values[0];
        }

        return DisplayCombination.Create(id, label, DisplayCombinationOrigin.User) with
        {
            Products = products,
            Category = display.Category,
            HiddenViewingGroups = hidden,
            ShownViewingGroups = shown,
            DisplayModes = display.ActiveDisplayModes
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase),
            LayerVisibility = new Dictionary<string, bool>(display.LayerVisibility, StringComparer.Ordinal),
            SubLayerVisibility = subLayers,
        };
    }

    /// <summary>
    /// Saves what the map shows as a new user combination named
    /// <paramref name="label"/> and makes it the active one (<b>Save current
    /// view as…</b>).
    /// </summary>
    public DisplayCombination SaveCurrent(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var combination = CaptureCurrent("user-" + Guid.NewGuid().ToString("N")[..12], label.Trim());
        _user.Add(combination);
        _active = combination;
        _settings.ActiveDisplayCombinationId = combination.Id;
        RegisterLoadPolicies();
        SaveUser();
        Recompute(force: true);
        return combination;
    }

    /// <summary>Renames a user combination; false for a built-in or an unknown id.</summary>
    public bool Rename(string id, string label)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var index = _user.FindIndex(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        if (index < 0) return false;
        var renamed = _user[index] with { Label = label.Trim() };
        _user[index] = renamed;
        if (_active is { } active && string.Equals(active.Id, id, StringComparison.Ordinal))
            _active = renamed;
        SaveUser();
        Recompute(force: true);
        return true;
    }

    /// <summary>
    /// Deletes a user combination; false for a built-in or an unknown id.
    /// Deleting the active one leaves the picture as it is, as <b>Custom</b>.
    /// </summary>
    public bool Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var index = _user.FindIndex(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        if (index < 0) return false;
        _user.RemoveAt(index);
        if (_active is { } active && string.Equals(active.Id, id, StringComparison.Ordinal))
        {
            _active = null;
            _settings.ActiveDisplayCombinationId = null;
            RegisterLoadPolicies();
        }
        SaveUser();
        Recompute(force: true);
        return true;
    }

    /// <summary>
    /// "Not in Pilotage" for a dataset the active combination leaves out and
    /// that is inactive; <see langword="null"/> otherwise.
    /// </summary>
    public string? InactiveReason(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_active is not { } active || active.Includes(DisplayCombinationProducts.KeyOf(entry)))
            return null;
        return _loader.GetActive(entry.Id.Value)
            ? null
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.DisplayCombination_NotInFormat, active.Label);
    }

    /// <summary>What the map shows now, as modified detection compares it.</summary>
    public DisplayPictureSnapshot Snapshot()
    {
        var datasets = new List<DisplayPictureDataset>();
        foreach (var entry in _datasets.Entries.ToArray())
        {
            if (!entry.IsLoaded) continue;
            var subLayers = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var subLayer in entry.SubLayers.ToArray())
                subLayers[subLayer.Key] = subLayer.IsVisible;
            datasets.Add(new DisplayPictureDataset(
                entry.Id.Value,
                DisplayCombinationProducts.KeyOf(entry),
                _loader.GetActive(entry.Id.Value),
                subLayers));
        }
        return new DisplayPictureSnapshot(_state.Snapshot(), datasets);
    }

    private void RegisterLoadPolicies()
    {
        if (_active is not { } active)
        {
            _loader.SetActiveOnLoad(null);
            _loader.SetSubLayerVisibilityOnLoad(null);
            return;
        }
        _loader.SetActiveOnLoad((entry, processor) =>
            active.Includes(DisplayCombinationProducts.KeyOf(entry.ProductSpec, processor.PortrayalSpec.Name)));
        _loader.SetSubLayerVisibilityOnLoad(active.SubLayerVisibility.Count == 0
            ? null
            : (_, key) => active.SubLayerVisibility.TryGetValue(key, out var visible) ? visible : null);
    }

    private void OnActiveChanged(string _) => Recompute();

    private void OnDatasetLoaded(DatasetEntry _) => RecomputeSoon();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RecomputeSoon();

    // Loads and layer-stack changes come in bursts; work the marker out once
    // per burst when a dispatcher is given.
    private void RecomputeSoon()
    {
        if (_post is null)
        {
            Recompute();
            return;
        }
        if (_recomputePending) return;
        _recomputePending = true;
        _post(() =>
        {
            _recomputePending = false;
            Recompute();
        });
    }

    private void Recompute() => Recompute(force: false);

    private void Recompute(bool force)
    {
        if (_applying > 0) return;
        var changes = _active is { } active
            ? DisplayCombinationComparison.Compare(active, Snapshot())
            : [];
        var same = changes.SequenceEqual(_changes);
        _changes = changes;
        UpdateInactiveReasons();
        if (force || !same)
            Changed?.Invoke();
    }

    private void UpdateInactiveReasons()
    {
        foreach (var entry in _datasets.Entries.ToArray())
            entry.InactiveReason = InactiveReason(entry);
    }

    private void SaveUser()
    {
        _settings.DisplayCombinations = [.. _user.Select(DisplayCombinationSettings.From)];
        Save();
    }

    private void Save()
    {
        try { _settings.Save(); } catch { /* best-effort, as the display state's own persistence */ }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _state.Changed -= Recompute;
        _loader.ActiveChanged -= OnActiveChanged;
        _loader.LayerStackChanged -= RecomputeSoon;
        _loader.DatasetLoaded -= OnDatasetLoaded;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        _loader.SetActiveOnLoad(null);
        _loader.SetSubLayerVisibilityOnLoad(null);
    }
}
