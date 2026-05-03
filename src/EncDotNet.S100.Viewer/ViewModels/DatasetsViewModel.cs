using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
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

    // ── Time-aware participation ──────────────────────────────────────
    //
    // Time-step navigation is global: per-entry prev/next/ComboBox
    // controls were replaced by a single timeline panel beneath the
    // map (TimelineView). Each entry exposes only:
    //   • the time samples the loader discovered for this dataset, and
    //   • the timestamp it is currently rendered at (a read-only label).

    private IReadOnlyList<DateTime>? _availableTimes;
    public IReadOnlyList<DateTime>? AvailableTimes
    {
        get => _availableTimes;
        set
        {
            if (SetProperty(ref _availableTimes, value))
                OnPropertyChanged(nameof(HasTimeSteps));
        }
    }

    /// <summary>True when this dataset has at least one time sample.</summary>
    public bool HasTimeSteps => _availableTimes is { Count: > 0 };

    private DateTime? _currentTime;
    /// <summary>
    /// The timestamp this dataset is currently rendered at, or
    /// <c>null</c> when the dataset is not time-aware or has not yet
    /// been rendered. Set by <see cref="IDatasetLoaderService"/>.
    /// </summary>
    public DateTime? CurrentTime
    {
        get => _currentTime;
        set
        {
            if (SetProperty(ref _currentTime, value))
                OnPropertyChanged(nameof(CurrentTimeLabel));
        }
    }

    /// <summary>
    /// Display label for <see cref="CurrentTime"/>, or empty when no
    /// time has been assigned. Formatted via
    /// <see cref="Strings.DatasetEntry_CurrentTimeFormat"/>.
    /// </summary>
    public string CurrentTimeLabel =>
        _currentTime is { } t
            ? string.Format(CultureInfo.CurrentCulture, Strings.DatasetEntry_CurrentTimeFormat, t)
            : string.Empty;

    public DatasetEntry(string filePath, string productSpec)
    {
        FilePath = filePath;
        ProductSpec = productSpec;
        DisplayName = System.IO.Path.GetFileName(filePath);
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

    public DatasetsViewModel(IDatasetLoaderService loader, GlobalTimeService? globalTime = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;

        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<DatasetEntry>(Remove);

        // Auto-unregister entries from the global time service when they
        // are removed from the collection.
        globalTime?.AttachTo(this);
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
