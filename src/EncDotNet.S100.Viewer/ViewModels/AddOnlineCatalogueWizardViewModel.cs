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

/// <summary>One step in a wizard's step header: its number, label, the choice made in it, and its state.</summary>
internal sealed class WizardStepViewModel : ViewModelBase
{
    private string _value = string.Empty;
    private WizardStepState _state;
    private bool _isSkipped;

    public WizardStepViewModel(int number, string label, Action<int> goTo)
    {
        Number = number;
        Label = label;
        GoToCommand = new RelayCommand(() => goTo(number), () => _state == WizardStepState.Done && !_isSkipped);
    }

    public int Number { get; }

    public string Label { get; }

    /// <summary>The choice made in the step ("NOAA ENC", "Everything", "New collection").</summary>
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

    /// <summary>True for a step the chosen catalogue doesn't need (Connection, for all but SECOM); it can't be clicked.</summary>
    public bool IsSkipped
    {
        get => _isSkipped;
        set
        {
            if (SetProperty(ref _isSkipped, value))
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
/// The "Add online catalogue" wizard: one dialog in four steps with a real
/// Back button. Step 1 picks a catalogue from the directory (or adds one by
/// URL); step 2, for a SECOM service, chooses the identity it presents and
/// says how it reads (#883); step 3 chooses what to include; step 4 picks the
/// collection and reviews. Other catalogues skip step 2. Nothing chosen is
/// lost going back: each catalogue keeps its own
/// <see cref="AddToLibraryDialogViewModel"/> (its ticks, scope and name), and
/// is read once (again when the identity changes).
/// </summary>
/// <remarks>
/// A SECOM service that can't be read yet is added anyway ("Add and wait"):
/// steps 3 and 4 then use the products from its directory entry, and the
/// source lists once it can be read.
/// </remarks>
internal sealed class AddOnlineCatalogueWizardViewModel : ViewModelBase
{
    /// <summary>The number of steps.</summary>
    public const int StepCount = 4;

    private readonly Func<AddToLibraryDialogViewModel> _scopeFactory;
    private readonly Func<KnownCatalogueSource, Action<Keys.KeysTab>, Secom.SecomConnectionStepViewModel?>? _connectionFactory;
    private readonly Action<Keys.KeysTab>? _openKeys;
    private readonly Dictionary<string, AddToLibraryDialogViewModel> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Secom.SecomConnectionStepViewModel> _connections = new(StringComparer.Ordinal);
    private Secom.SecomConnectionStepViewModel? _connection;
    private KnownCatalogueSource? _chosen;
    private Guid? _targetCollectionId;
    private int _currentStep = 1;
    private AddToLibraryDialogViewModel? _scope;

    public AddOnlineCatalogueWizardViewModel(
        CatalogueDirectoryDialogViewModel directory,
        Func<AddToLibraryDialogViewModel> scopeFactory,
        Action<Keys.KeysTab>? openKeys = null,
        Func<KnownCatalogueSource, Action<Keys.KeysTab>, Secom.SecomConnectionStepViewModel?>? connectionFactory = null)
    {
        _connectionFactory = connectionFactory;
        _openKeys = openKeys;
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        Directory = directory;
        // A registry service's "Set up an identity…" closes the wizard, then opens the page (#880).
        Directory.KeysRequested += (_, tab) => OpenKeys(tab);
        _scopeFactory = scopeFactory;

        Steps =
        [
            new(1, Strings.Wizard_StepCatalogue, GoTo),
            new(2, Strings.Wizard_StepConnection, GoTo),
            new(3, Strings.Wizard_StepInclude, GoTo),
            new(4, Strings.Wizard_StepAddTo, GoTo),
        ];

        BackCommand = new RelayCommand(() => GoTo(_currentStep == 3 && _connection is null ? 1 : _currentStep - 1), () => _currentStep > 1);
        NextCommand = new AsyncRelayCommand(NextAsync, () => CanNext, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AddCommand = new RelayCommand(() => _scope?.ConfirmCommand.Execute(null), () => CanAdd);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, false));
        TryAgainCommand = new AsyncRelayCommand(
            () => _scope?.LoadCatalogAsync() ?? Task.CompletedTask,
            () => _scope is { IsLoading: false, HasLoadError: true });
        ChangeCatalogueCommand = new RelayCommand(() => GoTo(1));

        Directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogueDirectoryDialogViewModel.SelectedEntry))
                Refresh();
        };
        Refresh();
    }

    /// <summary>Raised with <see langword="true"/> when the source was added, <see langword="false"/> when cancelled.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>Step 1: the catalogue directory.</summary>
    public CatalogueDirectoryDialogViewModel Directory { get; }

    /// <summary>Step 2 for a SECOM service: its identity and how it reads (#883); null for other catalogues.</summary>
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

    /// <summary>Steps 3 and 4: the scope and target for the chosen catalogue.</summary>
    public AddToLibraryDialogViewModel? Scope
    {
        get => _scope;
        private set => SetProperty(ref _scope, value);
    }

    /// <summary>The step header.</summary>
    public IReadOnlyList<WizardStepViewModel> Steps { get; }

    /// <summary>The step being shown (1–4).</summary>
    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (!SetProperty(ref _currentStep, value))
                return;

            OnPropertyChanged(nameof(IsCatalogueStep));
            OnPropertyChanged(nameof(IsConnectionStep));
            OnPropertyChanged(nameof(IsIncludeStep));
            OnPropertyChanged(nameof(IsAddToStep));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(StepOfText));
            Refresh();
        }
    }

    public bool IsCatalogueStep => _currentStep == 1;

    public bool IsConnectionStep => _currentStep == 2;

    public bool IsIncludeStep => _currentStep == 3;

    public bool IsAddToStep => _currentStep == 4;

    /// <summary>The primary button before the last step: "Next", or on the Connection step "Continue" or "Add and wait" (#883 C5).</summary>
    public string NextLabel => IsConnectionStep && _connection is { } connection ? connection.PrimaryLabel : Strings.Button_Next;

    /// <summary>True on the last step, where the primary button reads "Add to Library".</summary>
    public bool IsLastStep => _currentStep == StepCount;

    /// <summary>"Step 2 of 4".</summary>
    public string StepOfText => string.Format(CultureInfo.CurrentCulture, Strings.Wizard_StepOfFormat, _currentStep, StepCount);

    /// <summary>"Select at least one" when the scope step cannot continue for want of a tick; otherwise null.</summary>
    public string? FooterHint =>
        _currentStep == 3 && _scope is { IsLoaded: true, IsLoading: false, IncludeAll: false, HasSelection: false, IsSingleEntry: false }
            ? Strings.Wizard_SelectAtLeastOne
            : null;

    /// <summary>True when <see cref="FooterHint"/> is set.</summary>
    public bool HasFooterHint => FooterHint is not null;

    public ICommand BackCommand { get; }

    /// <summary>Steps 1–3: continues to the next step (reading the catalogue when it is first needed).</summary>
    public ICommand NextCommand { get; }

    /// <summary>Step 4: adds the source to the chosen collection.</summary>
    public ICommand AddCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Reads the catalogue again after a failure.</summary>
    public ICommand TryAgainCommand { get; }

    /// <summary>Goes back to the catalogue step.</summary>
    public ICommand ChangeCatalogueCommand { get; }

    /// <summary>Opens the wizard at step 1, adding into <paramref name="targetCollectionId"/> by default.</summary>
    public void Start(Guid? targetCollectionId)
    {
        _targetCollectionId = targetCollectionId;
        GoTo(1);
    }

    /// <summary>
    /// Opens the wizard at the include step with <paramref name="source"/> chosen
    /// (a shared feed, for example); step 1 shows it as done and stays reachable
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

    private bool CanNext => _currentStep switch
    {
        1 => Directory.CanContinueWithSelection,
        // Add and wait is always possible once the service has been asked (C5).
        2 => _connection is { HasState: true },
        3 => _scope is { CanContinueFromScope: true },
        _ => false,
    };

    private bool CanAdd => _currentStep == 4 && _scope is { ConfirmCommand: var confirm } && confirm.CanExecute(null);

    private Task NextAsync()
    {
        switch (_currentStep)
        {
            case 1 when Directory.SelectedEntry is { } entry:
                _chosen = entry.Source;
                if (entry.Source.Format == KnownCatalogueFormat.Secom && ConnectionFor(entry.Source) is { } connection)
                    return EnterConnectionAsync(connection);
                Connection = null;
                return EnterIncludeAsync(entry.Source);
            case 2 when CanNext && _chosen is { } secom:
                return EnterIncludeAsync(secom);
            case 3 when CanNext:
                GoTo(4);
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

    /// <summary>Shows step 2 for a SECOM service, probing it with the chosen identity the first time.</summary>
    private Task EnterConnectionAsync(Secom.SecomConnectionStepViewModel connection)
    {
        Connection = connection;
        CurrentStep = 2;
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
    /// Shows step 3 for <paramref name="source"/>. A catalogue is read unless it
    /// has been read (or is being read); a SECOM service is read with the
    /// Connection step's identity, or, when it can't be read, added to wait
    /// with its directory entry's products.
    /// </summary>
    private Task EnterIncludeAsync(KnownCatalogueSource source)
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

        Scope = scope;
        CurrentStep = 3;
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

    private void GoTo(int step)
    {
        if (step < 1 || step > StepCount || (step == 2 && _connection is null) || (step > 2 && _scope is null))
            return;

        CurrentStep = step;
        Refresh();
    }

    private void OnScopeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _scope))
            Refresh();
    }

    /// <summary>Updates the step header, the footer and the commands.</summary>
    private void Refresh()
    {
        foreach (var step in Steps)
        {
            step.State = step.Number < _currentStep ? WizardStepState.Done
                : step.Number == _currentStep ? WizardStepState.Current
                : WizardStepState.Upcoming;
        }

        Steps[0].Value = (_currentStep == 1 ? Directory.SelectedEntry?.Name : _chosen?.Name ?? _scope?.CatalogueName) ?? string.Empty;
        Steps[1].IsSkipped = _connection is null;
        Steps[1].Value = _connection is { } connection ? connection.Picker.Name
            : _currentStep > 2 ? Strings.Wizard_ConnectionNotNeeded
            : Strings.Wizard_ConnectionForSecom;
        Steps[2].Value = Steps[2].IsUpcoming ? Strings.Wizard_ChooseCatalogueFirst : _scope?.ScopeDescription ?? string.Empty;
        Steps[3].Value = Steps[3].IsUpcoming ? Strings.Wizard_Collection : _scope?.TargetDescription ?? string.Empty;

        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(HasFooterHint));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)NextCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)TryAgainCommand).NotifyCanExecuteChanged();
    }
}
