using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class DatasetEntry : ViewModelBase
{
    public string FilePath { get; }
    public string DisplayName { get; }
    public string ProductSpec { get; }

    private bool _isLoaded;
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    private string? _info;
    public string? Info
    {
        get => _info;
        set => SetProperty(ref _info, value);
    }

    // --- Time step support (S-111) ---

    private IReadOnlyList<DateTime>? _availableTimes;
    public IReadOnlyList<DateTime>? AvailableTimes
    {
        get => _availableTimes;
        set
        {
            if (SetProperty(ref _availableTimes, value))
            {
                OnPropertyChanged(nameof(HasTimeSteps));
                OnPropertyChanged(nameof(TimeStepMax));
                OnPropertyChanged(nameof(TimeStepLabels));
                OnPropertyChanged(nameof(TimeStepLabel));
                _previousTimeStepCommand.NotifyCanExecuteChanged();
                _nextTimeStepCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasTimeSteps => _availableTimes is { Count: > 1 };

    public int TimeStepMax => _availableTimes is { Count: > 0 } ? _availableTimes.Count - 1 : 0;

    private int _selectedTimeIndex;
    public int SelectedTimeIndex
    {
        get => _selectedTimeIndex;
        set
        {
            if (SetProperty(ref _selectedTimeIndex, value))
            {
                OnPropertyChanged(nameof(TimeStepLabel));
                _previousTimeStepCommand.NotifyCanExecuteChanged();
                _nextTimeStepCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string>? TimeStepLabels =>
        _availableTimes?.Select((t, i) => $"{i + 1}/{_availableTimes.Count}: {t:u}").ToList();

    public string TimeStepLabel =>
        _availableTimes is { Count: > 0 }
            ? $"{_selectedTimeIndex + 1}/{_availableTimes.Count}: {_availableTimes[_selectedTimeIndex]:u}"
            : "";

    private readonly RelayCommand _previousTimeStepCommand;
    private readonly RelayCommand _nextTimeStepCommand;

    public ICommand PreviousTimeStepCommand => _previousTimeStepCommand;
    public ICommand NextTimeStepCommand => _nextTimeStepCommand;

    public DatasetEntry(string filePath, string productSpec)
    {
        FilePath = filePath;
        ProductSpec = productSpec;
        DisplayName = System.IO.Path.GetFileName(filePath);

        _previousTimeStepCommand = new RelayCommand(
            () => { if (_selectedTimeIndex > 0) SelectedTimeIndex--; },
            () => _selectedTimeIndex > 0);

        _nextTimeStepCommand = new RelayCommand(
            () => { if (_availableTimes is not null && _selectedTimeIndex < _availableTimes.Count - 1) SelectedTimeIndex++; },
            () => _availableTimes is not null && _selectedTimeIndex < _availableTimes.Count - 1);
    }
}

internal sealed class DatasetsViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;

    public ObservableCollection<DatasetEntry> Entries { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>
    /// Raised when <see cref="LoadFromPathAsync"/> rejects a file because
    /// no S-100 product specification recognised its extension. The window
    /// surfaces this as a status-bar message.
    /// </summary>
    public event Action<string>? UnrecognizedFileEncountered;

    public DatasetsViewModel(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;

        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<DatasetEntry>(Remove);
    }

    public DatasetEntry Add(string filePath, string productSpec)
    {
        var entry = new DatasetEntry(filePath, productSpec);
        Entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Loads the supplied entry through the dataset loader. Fire-and-forget;
    /// errors are surfaced via <see cref="IDatasetLoaderService.StatusChanged"/>.
    /// </summary>
    public void RequestLoad(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = _loader.LoadAsync(entry);
    }

    /// <summary>
    /// Detects the product spec for <paramref name="path"/>, adds an entry,
    /// and asks the loader to render it. If the file extension is not
    /// recognised, raises <see cref="UnrecognizedFileEncountered"/> with the
    /// extension and returns <c>null</c>.
    /// </summary>
    public async Task<DatasetEntry?> LoadFromPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var spec = Datasets.Pipelines.DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            UnrecognizedFileEncountered?.Invoke(System.IO.Path.GetExtension(path));
            return null;
        }

        var entry = Add(path, spec);
        await _loader.LoadAsync(entry);
        return entry;
    }

    private void Remove(DatasetEntry? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry);
    }
}
