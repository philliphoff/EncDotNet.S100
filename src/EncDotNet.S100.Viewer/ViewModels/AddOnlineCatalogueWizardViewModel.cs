using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Where a wizard step stands relative to the current one.</summary>
internal enum WizardStepState
{
    /// <summary>A later step; it cannot be clicked.</summary>
    Upcoming,

    /// <summary>The step being shown.</summary>
    Current,

    /// <summary>An earlier, completed step; clicking it goes back to it.</summary>
    Done,
}

/// <summary>What a step of the "Add online source" wizard is for.</summary>
internal enum WizardStepKind
{
    /// <summary>Choose the source from the directory (or add one by URL).</summary>
    Source,

    /// <summary>SECOM services only: the identity presented and how the service reads (#883).</summary>
    Connection,

    /// <summary>Choose what to include.</summary>
    Include,

    /// <summary>Choose the collection and review.</summary>
    AddTo,
}

/// <summary>One step in a wizard's step header: its number, label, the choice made in it, and its state.</summary>
internal sealed class WizardStepViewModel : ViewModelBase
{
    private string _value = string.Empty;
    private WizardStepState _state;

    public WizardStepViewModel(WizardStepKind kind, int number, string label, Action<int> goTo)
    {
        Kind = kind;
        Number = number;
        Label = label;
        GoToCommand = new RelayCommand(() => goTo(number), () => _state == WizardStepState.Done);
    }

    /// <summary>What the step is for.</summary>
    public WizardStepKind Kind { get; }

    /// <summary>The step's position in the current path (1-based).</summary>
    public int Number { get; }

    public string Label { get; }

    /// <summary>The choice made in a completed step ("NOAA ENC", "Everything", "New collection"); empty otherwise.</summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public WizardStepState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
                return;

            OnPropertyChanged(nameof(IsCurrent));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsUpcoming));
            ((RelayCommand)GoToCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsCurrent => _state == WizardStepState.Current;

    public bool IsDone => _state == WizardStepState.Done;

    public bool IsUpcoming => _state == WizardStepState.Upcoming;

    /// <summary>Goes back to the step; only a completed step can be clicked.</summary>
    public ICommand GoToCommand { get; }
}

/// <summary>
/// The "Add online source" wizard: one dialog with a real Back button, in
/// three steps (Source, What to include, Add to), or four for a SECOM service,
/// whose Connection step chooses the identity it presents and says how it
/// reads (#883). The step list follows the path: it is rebuilt when the
/// source chosen in step 1 changes between SECOM and anything else, and steps
/// are numbered by their position in it. Nothing chosen is lost going back: each catalogue keeps its own
/// <see cref="AddToLibraryDialogViewModel"/> (its ticks, scope and name), and
/// is read once (again when the identity changes).
/// </summary>
/// <remarks>
/// A SECOM service that can't be read yet is added anyway ("Add and wait"):
/// What to include and Add to then use the products from its directory entry, and the
/// source lists once it can be read.
/// </remarks>
internal sealed class AddOnlineCatalogueWizardViewModel : ViewModelBase
{
    private readonly Func<AddToLibraryDialogViewModel> _scopeFactory;
    private readonly Func<KnownCatalogueSource, Action<Keys.KeysTab>, Secom.SecomConnectionStepViewModel?>? _connectionFactory;
    private readonly Action<Keys.KeysTab>? _openKeys;
    private readonly Dictionary<string, AddToLibraryDialogViewModel> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Secom.SecomConnectionStepViewModel> _connections = new(StringComparer.Ordinal);
    private Secom.SecomConnectionStepViewModel? _connection;
    private KnownCatalogueSource? _chosen;
    private Guid? _targetCollectionId;
    private WizardStepKind _current = WizardStepKind.Source;
    private IReadOnlyList<WizardStepViewModel> _steps = [];
    private AddToLibraryDialogViewModel? _scope;

    public AddOnlineCatalogueWizardViewModel(
        CatalogueDirectoryDialogViewModel directory,
        Func<AddToLibraryDialogViewModel> scopeFactory,
        Action<Keys.KeysTab>? openKeys,
        Func<KnownCatalogueSource, Action<Keys.KeysTab>, Secom.SecomConnectionStepViewModel?>? connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _openKeys = openKeys;
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        Directory = directory;
        // A registry service's "Set up an identity…" closes the wizard, then opens the page (#880).
        Directory.KeysRequested += (_, tab) => OpenKeys(tab);
        _scopeFactory = scopeFactory;

        BackCommand = new RelayCommand(() => GoTo(CurrentStep - 1), () => CurrentStep > 1);
        NextCommand = new AsyncRelayCommand(NextAsync, () => CanNext, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AddCommand = new RelayCommand(() => _scope?.ConfirmCommand.Execute(null), () => CanAdd);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, false));
        TryAgainCommand = new AsyncRelayCommand(
            () => _scope?.LoadCatalogAsync() ?? Task.CompletedTask,
            () => _scope is { IsLoading: false, HasLoadError: true });
        ChangeCatalogueCommand = new RelayCommand(() => GoTo(1));

        Directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CatalogueDirectoryDialogViewModel.SelectedEntry)
                or nameof(CatalogueDirectoryDialogViewModel.CanContinueWithSelection))
            {
                Refresh();
            }
        };
        Refresh();
    }

    /// <summary>Raised with <see langword="true"/> when the source was added, <see langword="false"/> when cancelled.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>The Source step: the catalogue directory.</summary>
    public CatalogueDirectoryDialogViewModel Directory { get; }

    /// <summary>The Connection step of a SECOM service: its identity and how it reads (#883); null for other sources.</summary>
    public Secom.SecomConnectionStepViewModel? Connection
    {
        get => _connection;
        private set
        {
            if (ReferenceEquals(_connection, value))
                return;
            if (_connection is not null)
                _connection.StateChanged -= OnConnectionChanged;
            _connection = value;
            if (_connection is not null)
                _connection.StateChanged += OnConnectionChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>What to include and Add to: the scope and target for the chosen source.</summary>
    public AddToLibraryDialogViewModel? Scope
    {
        get => _scope;
        private set => SetProperty(ref _scope, value);
    }

    /// <summary>The step header for the current path: three steps, or four for a SECOM service.</summary>
    public IReadOnlyList<WizardStepViewModel> Steps
    {
        get => _steps;
        private set => SetProperty(ref _steps, value);
    }

    /// <summary>The number of steps in the current path.</summary>
    public int StepCount => _steps.Count;

    /// <summary>The step being shown, as its position in <see cref="Steps"/> (1-based).</summary>
    public int CurrentStep => IndexOf(_current) + 1;

    /// <summary>What the step being shown is for.</summary>
    public WizardStepKind CurrentKind
    {
        get => _current;
        private set
        {
            if (!SetProperty(ref _current, value))
                return;

            OnPropertyChanged(nameof(IsCatalogueStep));
            OnPropertyChanged(nameof(IsConnectionStep));
            OnPropertyChanged(nameof(IsIncludeStep));
            OnPropertyChanged(nameof(IsAddToStep));
            OnPropertyChanged(nameof(IsLastStep));
            Refresh();
        }
    }

    public bool IsCatalogueStep => _current == WizardStepKind.Source;

    public bool IsConnectionStep => _current == WizardStepKind.Connection;

    public bool IsIncludeStep => _current == WizardStepKind.Include;

    public bool IsAddToStep => _current == WizardStepKind.AddTo;

    /// <summary>The primary button before the last step: "Next", or on the Connection step "Continue" or "Add and wait" (#883 C5).</summary>
    public string NextLabel => IsConnectionStep && _connection is { } connection ? connection.PrimaryLabel : Strings.Button_Next;

    /// <summary>True on the last step, where the primary button reads "Add to Library".</summary>
    public bool IsLastStep => _current == WizardStepKind.AddTo;

    /// <summary>"Step 2 of 3", counted in the current path.</summary>
    public string StepOfText => string.Format(CultureInfo.CurrentCulture, Strings.Wizard_StepOfFormat, CurrentStep, StepCount);

    /// <summary>
    /// On the Source step, the selection in one line ("NOAA ENC — all U.S. waters · NOAA ENC"),
    /// so the footer confirms the choice when its row has scrolled away; otherwise null.
    /// </summary>
    public string? SelectionText =>
        _isReadingCatalogue ? Strings.Wizard_Loading
        : IsCatalogueStep && Directory.SelectedEntry is { } entry
            ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_SelectionFormat, entry.Name, entry.FormatText)
            : null;

    /// <summary>True when <see cref="SelectionText"/> is set.</summary>
    public bool HasSelectionText => SelectionText is not null;

    /// <summary>"Select at least one" when the scope step cannot continue for want of a tick; otherwise null.</summary>
    public string? FooterHint =>
        IsIncludeStep && _scope is { IsLoaded: true, IsLoading: false, IncludeAll: false, HasSelection: false, IsSingleEntry: false }
            ? Strings.Wizard_SelectAtLeastOne
            : null;

    /// <summary>True when <see cref="FooterHint"/> is set.</summary>
    public bool HasFooterHint => FooterHint is not null;

    public ICommand BackCommand { get; }

    /// <summary>Before the last step: continues to the next one (reading the catalogue when it is first needed).</summary>
    public ICommand NextCommand { get; }

    /// <summary>The last step: adds the source to the chosen collection.</summary>
    public ICommand AddCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Reads the catalogue again after a failure.</summary>
    public ICommand TryAgainCommand { get; }

    /// <summary>Goes back to the Source step.</summary>
    public ICommand ChangeCatalogueCommand { get; }

    /// <summary>Opens the wizard at the Source step, adding into <paramref name="targetCollectionId"/> by default.</summary>
    public void Start(Guid? targetCollectionId)
    {
        _targetCollectionId = targetCollectionId;
        CurrentKind = WizardStepKind.Source;
        Refresh();
    }

    /// <summary>
    /// Opens the wizard at the include step with <paramref name="source"/> chosen
    /// (a shared feed, for example); the Source step shows it as done and stays reachable
    /// with Back. Completes when the catalogue has been read.
    /// </summary>
    public Task StartAtIncludeAsync(KnownCatalogueSource source, Guid? targetCollectionId)
    {
        ArgumentNullException.ThrowIfNull(source);

        _targetCollectionId = targetCollectionId;
        Directory.Preselect(source);
        _chosen = source;
        Connection = null;
        return EnterIncludeAsync(source);
    }

    /// <summary>
    /// True when the path has a Connection step: on the Source step, when the
    /// selection is a SECOM service the wizard can connect to; after it, when
    /// the chosen source has one.
    /// </summary>
    private bool HasConnectionStep => IsCatalogueStep
        ? Directory.SelectedEntry?.Source is { Format: KnownCatalogueFormat.Secom } && _connectionFactory is not null
        : _connection is not null;

    /// <summary>
    /// True while Next on the Source step reads the chosen catalogue (D12); the
    /// footer says so and Next waits.
    /// </summary>
    public bool IsReadingCatalogue
    {
        get => _isReadingCatalogue;
        private set
        {
            if (!SetProperty(ref _isReadingCatalogue, value))
                return;
            Refresh();
        }
    }

    private bool _isReadingCatalogue;

    private bool CanNext => _current switch
    {
        WizardStepKind.Source => Directory.CanContinueWithSelection && !_isReadingCatalogue,
        // Add and wait is always possible once the service has been asked (C5).
        WizardStepKind.Connection => _connection is { HasState: true },
        WizardStepKind.Include => _scope is { CanContinueFromScope: true },
        _ => false,
    };

    private bool CanAdd => IsAddToStep && _scope is { ConfirmCommand: var confirm } && confirm.CanExecute(null);

    private Task NextAsync()
    {
        switch (_current)
        {
            case WizardStepKind.Source when Directory.SelectedEntry is { } entry:
                _chosen = entry.Source;
                if (entry.Source.Format == KnownCatalogueFormat.Secom && ConnectionFor(entry.Source) is { } connection)
                    return EnterConnectionAsync(connection);
                Connection = null;
                return ReadThenContinueAsync(entry.Source);
            case WizardStepKind.Connection when CanNext && _chosen is { } secom:
                return EnterIncludeAsync(secom);
            case WizardStepKind.Include when CanNext:
                CurrentKind = WizardStepKind.AddTo;
                break;
        }

        return Task.CompletedTask;
    }

    private Secom.SecomConnectionStepViewModel? ConnectionFor(KnownCatalogueSource source)
    {
        if (_connections.TryGetValue(source.Id, out var known))
            return known;
        if (_connectionFactory?.Invoke(source, OpenKeys) is not { } created)
            return null;
        _connections[source.Id] = created;
        return created;
    }

    /// <summary>Shows the Connection step for a SECOM service, probing it with the chosen identity the first time.</summary>
    private Task EnterConnectionAsync(Secom.SecomConnectionStepViewModel connection)
    {
        Connection = connection;
        CurrentKind = WizardStepKind.Connection;
        Refresh();
        return connection.HasState || connection.IsProbing ? Task.CompletedTask : connection.ProbeAsync();
    }

    /// <summary>Closes the wizard, then opens Settings → Keys &amp; certificates on <paramref name="tab"/> (#880).</summary>
    private void OpenKeys(Keys.KeysTab tab)
    {
        Closed?.Invoke(this, false);
        _openKeys?.Invoke(tab);
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _connection))
            Refresh();
    }

    /// <summary>
    /// Shows What to include for <paramref name="source"/>. A catalogue is read unless it
    /// has been read (or is being read); a SECOM service is read with the
    /// Connection step's identity, or, when it can't be read, added to wait
    /// with its directory entry's products.
    /// </summary>
    private Task EnterIncludeAsync(KnownCatalogueSource source)
    {
        var scope = ScopeFor(source);
        Scope = scope;
        CurrentKind = WizardStepKind.Include;
        Refresh();
        if (_connection is { } connection && scope.IsSecom)
        {
            if (connection.IsReadable)
                return scope.ReadSecomWithAsync(connection.IdentitySetting);
            scope.WaitForSecom(connection.IdentitySetting, connection.Products);
            Refresh();
            return Task.CompletedTask;
        }

        return scope.IsLoaded || scope.IsLoading ? Task.CompletedTask : scope.LoadCatalogAsync();
    }

    /// <summary>
    /// From the Source step (not SECOM): reads the catalogue while Next shows
    /// that it is reading, then goes to What to include, or, when the catalogue
    /// lists one download, straight to Add to with What to include done (D12).
    /// A failed read shows What to include with the error and Try again.
    /// </summary>
    private async Task ReadThenContinueAsync(KnownCatalogueSource source)
    {
        var scope = ScopeFor(source);
        Scope = scope;
        if (!scope.IsLoaded && !scope.HasLoadError)
        {
            IsReadingCatalogue = true;
            try
            {
                await (scope.IsLoading ? WaitForLoadAsync(scope) : scope.LoadCatalogAsync());
            }
            finally
            {
                IsReadingCatalogue = false;
            }
        }

        // The user may have gone elsewhere (closed, or chosen another source) meanwhile.
        if (_current != WizardStepKind.Source || Directory.SelectedEntry?.Source.Id != source.Id)
            return;

        CurrentKind = scope is { IsLoaded: true, IsSingleEntry: true, HasLoadError: false }
            ? WizardStepKind.AddTo
            : WizardStepKind.Include;
        Refresh();
    }

    /// <summary>Completes when a read already under way ends.</summary>
    private static Task WaitForLoadAsync(AddToLibraryDialogViewModel scope)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!scope.IsLoading)
            {
                scope.PropertyChanged -= OnChanged;
                done.TrySetResult();
            }
        }

        scope.PropertyChanged += OnChanged;
        if (!scope.IsLoading)
        {
            scope.PropertyChanged -= OnChanged;
            done.TrySetResult();
        }

        return done.Task;
    }

    /// <summary>The scope kept for <paramref name="source"/>, created the first time it is chosen.</summary>
    private AddToLibraryDialogViewModel ScopeFor(KnownCatalogueSource source)
    {
        if (!_scopes.TryGetValue(source.Id, out var scope))
        {
            scope = _scopeFactory();
            scope.Initialize(source, _targetCollectionId);
            scope.PropertyChanged += OnScopeChanged;
            scope.Closed += (_, confirmed) =>
            {
                if (confirmed)
                    Closed?.Invoke(this, true);
            };
            _scopes[source.Id] = scope;
        }

        return scope;
    }

    /// <summary>Goes to the step at position <paramref name="step"/> (1-based) in the current path.</summary>
    private void GoTo(int step)
    {
        if (step < 1 || step > _steps.Count)
            return;

        var kind = _steps[step - 1].Kind;
        if ((kind == WizardStepKind.Connection && _connection is null)
            || (kind is WizardStepKind.Include or WizardStepKind.AddTo && _scope is null))
        {
            return;
        }

        CurrentKind = kind;
        Refresh();
    }

    private int IndexOf(WizardStepKind kind)
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            if (_steps[i].Kind == kind)
                return i;
        }

        return -1;
    }

    /// <summary>Rebuilds <see cref="Steps"/> when the path gains or loses its Connection step.</summary>
    private void RebuildSteps()
    {
        var withConnection = HasConnectionStep;
        if (_steps.Count == (withConnection ? 4 : 3))
            return;

        var kinds = withConnection
            ? new[] { WizardStepKind.Source, WizardStepKind.Connection, WizardStepKind.Include, WizardStepKind.AddTo }
            : new[] { WizardStepKind.Source, WizardStepKind.Include, WizardStepKind.AddTo };
        Steps = kinds.Select((kind, i) => new WizardStepViewModel(kind, i + 1, LabelOf(kind), GoTo)).ToArray();
        OnPropertyChanged(nameof(StepCount));
    }

    private static string LabelOf(WizardStepKind kind) => kind switch
    {
        WizardStepKind.Source => Strings.Wizard_StepCatalogue,
        WizardStepKind.Connection => Strings.Wizard_StepConnection,
        WizardStepKind.Include => Strings.Wizard_StepInclude,
        _ => Strings.Wizard_StepAddTo,
    };

    /// <summary>The choice made in a completed step of <paramref name="kind"/>.</summary>
    private string DoneValueOf(WizardStepKind kind) => kind switch
    {
        WizardStepKind.Source => _chosen?.Name ?? _scope?.CatalogueName ?? string.Empty,
        WizardStepKind.Connection => _connection?.Picker.Name ?? string.Empty,
        WizardStepKind.Include => _scope?.ScopeDescription ?? string.Empty,
        _ => _scope?.TargetDescription ?? string.Empty,
    };

    private void OnScopeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _scope))
            Refresh();
    }

    /// <summary>Updates the step header, the footer and the commands.</summary>
    private void Refresh()
    {
        RebuildSteps();
        var current = CurrentStep;
        foreach (var step in _steps)
        {
            step.State = step.Number < current ? WizardStepState.Done
                : step.Number == current ? WizardStepState.Current
                : WizardStepState.Upcoming;
            // Only a completed step says what was chosen in it (D4).
            step.Value = step.IsDone ? DoneValueOf(step.Kind) : string.Empty;
        }

        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StepOfText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelectionText));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(HasFooterHint));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)NextCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)TryAgainCommand).NotifyCanExecuteChanged();
    }
}
