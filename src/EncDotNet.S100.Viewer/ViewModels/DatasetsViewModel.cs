using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

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
    public ObservableCollection<DatasetEntry> Entries { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    public event Action<DatasetEntry>? LoadRequested;

    public DatasetsViewModel()
    {
        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<DatasetEntry>(Remove);
    }

    public DatasetEntry Add(string filePath, string productSpec)
    {
        var entry = new DatasetEntry(filePath, productSpec);
        Entries.Add(entry);
        return entry;
    }

    public void RequestLoad(DatasetEntry entry)
    {
        LoadRequested?.Invoke(entry);
    }

    private void Remove(DatasetEntry? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry);
    }
}
