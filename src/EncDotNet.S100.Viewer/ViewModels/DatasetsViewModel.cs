using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

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

    public DatasetEntry(string filePath, string productSpec)
    {
        FilePath = filePath;
        ProductSpec = productSpec;
        DisplayName = System.IO.Path.GetFileName(filePath);
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
