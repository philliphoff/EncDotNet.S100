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

    public WizardStepViewModel(int number, string label, Action<int> goTo)
    {
        Number = number;
        Label = label;
        GoToCommand = new RelayCommand(() => goTo(number), () => _state == WizardStepState.Done);
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

    public bool IsCurrent => _state == WizardStepState.Current;

    public bool IsDone => _state == WizardStepState.Done;

    public bool IsUpcoming => _state == WizardStepState.Upcoming;

    /// <summary>Goes back to the step; only a completed step can be clicked.</summary>
    public ICommand GoToCommand { get; }
}

/// <summary>
/// The "Add online catalogue" wizard: one dialog in three steps with a real
/// Back button. Step 1 picks a catalogue from the directory (or adds one by
/// URL); step 2 chooses what to include; step 3 picks the collection and
/// reviews. Nothing chosen is lost going back: each catalogue keeps its own
/// <see cref="AddToLibraryDialogViewModel"/> (its ticks, scope and name), and
/// is read once.
/// </summary>
internal sealed class AddOnlineCatalogueWizardViewModel : ViewModelBase
{
    /// <summary>The number of steps.</summary>
    public const int StepCount = 3;

    private readonly Func<AddToLibraryDialogViewModel> _scopeFactory;
    private readonly Dictionary<string, AddToLibraryDialogViewModel> _scopes = new(StringComparer.Ordinal);
    private Guid? _targetCollectionId;
    private int _currentStep = 1;
    private AddToLibraryDialogViewModel? _scope;

    public AddOnlineCatalogueWizardViewModel(
        CatalogueDirectoryDialogViewModel directory, Func<AddToLibraryDialogViewModel> scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        Directory = directory;
        _scopeFactory = scopeFactory;

        Steps =
        [
            new(1, Strings.Wizard_StepCatalogue, GoTo),
            new(2, Strings.Wizard_StepInclude, GoTo),
            new(3, Strings.Wizard_StepAddTo, GoTo),
        ];

        BackCommand = new RelayCommand(() => GoTo(_currentStep - 1), () => _currentStep > 1);
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

    /// <summary>Steps 2 and 3: the scope and target for the chosen catalogue.</summary>
    public AddToLibraryDialogViewModel? Scope
    {
        get => _scope;
        private set => SetProperty(ref _scope, value);
    }

    /// <summary>The step header.</summary>
    public IReadOnlyList<WizardStepViewModel> Steps { get; }

    /// <summary>The step being shown (1–3).</summary>
    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (!SetProperty(ref _currentStep, value))
                return;

            OnPropertyChanged(nameof(IsCatalogueStep));
            OnPropertyChanged(nameof(IsIncludeStep));
            OnPropertyChanged(nameof(IsAddToStep));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(StepOfText));
            Refresh();
        }
    }

    public bool IsCatalogueStep => _currentStep == 1;

    public bool IsIncludeStep => _currentStep == 2;

    public bool IsAddToStep => _currentStep == 3;

    /// <summary>True on the last step, where the primary button reads "Add to Library".</summary>
    public bool IsLastStep => _currentStep == StepCount;

    /// <summary>"Step 2 of 3".</summary>
    public string StepOfText => string.Format(CultureInfo.CurrentCulture, Strings.Wizard_StepOfFormat, _currentStep, StepCount);

    /// <summary>"Select at least one" when the scope step cannot continue for want of a tick; otherwise null.</summary>
    public string? FooterHint =>
        _currentStep == 2 && _scope is { IsLoaded: true, IsLoading: false, IncludeAll: false, HasSelection: false, IsSingleEntry: false }
            ? Strings.Wizard_SelectAtLeastOne
            : null;

    /// <summary>True when <see cref="FooterHint"/> is set.</summary>
    public bool HasFooterHint => FooterHint is not null;

    public ICommand BackCommand { get; }

    /// <summary>Steps 1–2: continues to the next step (step 1 reads the catalogue if not read yet).</summary>
    public ICommand NextCommand { get; }

    /// <summary>Step 3: adds the source to the chosen collection.</summary>
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
    /// Opens the wizard at step 2 with <paramref name="source"/> chosen (a
    /// shared feed, for example); step 1 shows it as done and stays reachable
    /// with Back. Completes when the catalogue has been read.
    /// </summary>
    public Task StartAtIncludeAsync(KnownCatalogueSource source, Guid? targetCollectionId)
    {
        ArgumentNullException.ThrowIfNull(source);

        _targetCollectionId = targetCollectionId;
        Directory.Preselect(source);
        return EnterIncludeAsync(source);
    }

    private bool CanNext => _currentStep switch
    {
        1 => Directory.SelectedEntry is not null,
        2 => _scope is { CanContinueFromScope: true },
        _ => false,
    };

    private bool CanAdd => _currentStep == 3 && _scope is { ConfirmCommand: var confirm } && confirm.CanExecute(null);

    private Task NextAsync()
    {
        switch (_currentStep)
        {
            case 1 when Directory.SelectedEntry is { } entry:
                return EnterIncludeAsync(entry.Source);
            case 2 when CanNext:
                GoTo(3);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Shows step 2 for <paramref name="source"/>, reading its catalogue unless it has been read (or is being read).</summary>
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
        CurrentStep = 2;
        Refresh();
        return scope.IsLoaded || scope.IsLoading ? Task.CompletedTask : scope.LoadCatalogAsync();
    }

    private void GoTo(int step)
    {
        if (step < 1 || step > StepCount || (step > 1 && _scope is null))
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

        Steps[0].Value = (_currentStep == 1 ? Directory.SelectedEntry?.Name : _scope?.CatalogueName) ?? string.Empty;
        Steps[1].Value = Steps[1].IsUpcoming ? Strings.Wizard_ChooseCatalogueFirst : _scope?.ScopeDescription ?? string.Empty;
        Steps[2].Value = Steps[2].IsUpcoming ? Strings.Wizard_Collection : _scope?.TargetDescription ?? string.Empty;

        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(HasFooterHint));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)NextCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)TryAgainCommand).NotifyCanExecuteChanged();
    }
}
