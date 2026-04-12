using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class FeatureCataloguesViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;

    public ObservableCollection<CatalogueEntry> Entries { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    public FeatureCataloguesViewModel(ViewerSettings settings)
    {
        _settings = settings;
        AddCommand = new RelayCommand<string?>(_ => { }); // wired up from view
        RemoveCommand = new RelayCommand<CatalogueEntry>(Remove);
        Reload();
    }

    public void Reload()
    {
        Entries.Clear();
        foreach (var (spec, path) in _settings.FeatureCataloguePaths.OrderBy(kv => kv.Key))
        {
            Entries.Add(new CatalogueEntry(spec, path));
        }
    }

    public void AddOrUpdate(string spec, string path)
    {
        _settings.FeatureCataloguePaths[spec] = path;
        _settings.Save();
        Reload();
    }

    /// <summary>
    /// Adds a catalogue entry for the current session only, without persisting to settings.
    /// </summary>
    public void AddTransient(string spec, string path)
    {
        Entries.Add(new CatalogueEntry(spec, path));
    }

    /// <summary>
    /// Adds a built-in catalogue entry that cannot be removed by the user.
    /// Skipped if a user-provided entry already exists for the spec.
    /// </summary>
    public void AddBuiltIn(string spec, string displayPath)
    {
        if (!Entries.Any(e => e.ProductSpec.Equals(spec, StringComparison.OrdinalIgnoreCase)))
        {
            Entries.Add(new CatalogueEntry(spec, displayPath, isBuiltIn: true));
        }
    }

    private void Remove(CatalogueEntry? entry)
    {
        if (entry is null || entry.IsBuiltIn) return;
        _settings.FeatureCataloguePaths.Remove(entry.ProductSpec);
        _settings.Save();
        Reload();
    }
}

internal sealed class PortrayalCataloguesViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _catalogueManager;

    public ObservableCollection<CatalogueEntry> Entries { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    public PortrayalCataloguesViewModel(ViewerSettings settings, PortrayalCatalogueManager catalogueManager)
    {
        _settings = settings;
        _catalogueManager = catalogueManager;
        AddCommand = new RelayCommand<string?>(_ => { });
        RemoveCommand = new RelayCommand<CatalogueEntry>(Remove);
        Reload();
    }

    public void Reload()
    {
        Entries.Clear();
        foreach (var (spec, path) in _settings.CataloguePaths.OrderBy(kv => kv.Key))
        {
            Entries.Add(new CatalogueEntry(spec, path));
        }
    }

    public void AddOrUpdate(string spec, string path)
    {
        _catalogueManager.SetPath(spec, path);
        _settings.CataloguePaths[spec] = path;
        _settings.Save();
        Reload();
    }

    /// <summary>
    /// Adds a catalogue entry for the current session only, without persisting to settings.
    /// </summary>
    public void AddTransient(string spec, string path)
    {
        _catalogueManager.SetPath(spec, path);
        Entries.Add(new CatalogueEntry(spec, path));
    }

    /// <summary>
    /// Adds a built-in catalogue entry that cannot be removed by the user.
    /// Skipped if a user-provided entry already exists for the spec.
    /// </summary>
    public void AddBuiltIn(string spec, string displayPath)
    {
        if (!Entries.Any(e => e.ProductSpec.Equals(spec, StringComparison.OrdinalIgnoreCase)))
        {
            Entries.Add(new CatalogueEntry(spec, displayPath, isBuiltIn: true));
        }
    }

    private void Remove(CatalogueEntry? entry)
    {
        if (entry is null || entry.IsBuiltIn) return;
        _settings.CataloguePaths.Remove(entry.ProductSpec);
        _settings.Save();
        Reload();
    }
}

internal sealed class CatalogueEntry
{
    public string ProductSpec { get; }
    public string Path { get; }
    public bool IsBuiltIn { get; }
    public string DisplayName => $"{ProductSpec} — {System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar))}";

    public CatalogueEntry(string productSpec, string path, bool isBuiltIn = false)
    {
        ProductSpec = productSpec;
        Path = path;
        IsBuiltIn = isBuiltIn;
    }
}
