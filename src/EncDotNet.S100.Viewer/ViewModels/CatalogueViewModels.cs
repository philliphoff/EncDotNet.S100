using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class FeatureCataloguesViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly Services.IS100ExaminerLinkBuilder? _examinerLinks;
    private readonly Services.IUrlOpener? _urlOpener;

    public ObservableCollection<CatalogueEntry> Entries { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>
    /// Opens the row's catalogue in the S-100 Feature Catalogue eXaminer
    /// (issue #442). No-op when the examiner integration is unavailable.
    /// </summary>
    public ICommand OpenInExaminerCommand { get; }

    public FeatureCataloguesViewModel(ViewerSettings settings)
        : this(settings, examinerLinks: null, urlOpener: null)
    {
    }

    public FeatureCataloguesViewModel(
        ViewerSettings settings,
        Services.IS100ExaminerLinkBuilder? examinerLinks,
        Services.IUrlOpener? urlOpener)
    {
        _settings = settings;
        _examinerLinks = examinerLinks;
        _urlOpener = urlOpener;
        AddCommand = new RelayCommand<string?>(_ => { }); // wired up from view
        RemoveCommand = new RelayCommand<CatalogueEntry>(Remove);
        OpenInExaminerCommand = new RelayCommand<CatalogueEntry>(OpenInExaminer);
        Reload();
    }

    private void OpenInExaminer(CatalogueEntry? entry)
    {
        if (entry is null || _examinerLinks is null || _urlOpener is null)
            return;
        var url = _examinerLinks.BuildCatalogueUrl(entry.ProductSpec);
        if (!string.IsNullOrEmpty(url))
            _urlOpener.Open(url);
    }

    public void Reload()
    {
        Entries.Clear();
        foreach (var (spec, path) in _settings.FeatureCataloguePaths.OrderBy(kv => kv.Key))
        {
            Entries.Add(CreateEntry(spec, path, isBuiltIn: false));
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
        Entries.Add(CreateEntry(spec, path, isBuiltIn: false));
    }

    /// <summary>
    /// Adds a built-in catalogue entry that cannot be removed by the user.
    /// Skipped if a user-provided entry already exists for the spec.
    /// </summary>
    public void AddBuiltIn(string spec, string displayPath, string? version = null, string? versionDate = null)
    {
        if (!Entries.Any(e => e.ProductSpec.Equals(spec, StringComparison.OrdinalIgnoreCase)))
        {
            // Built-in specs are always covered by the curated title map, so the
            // parsed-name fallback never fires here.
            var title = ComposeTitle(spec, Strings.SpecDisplayName(spec));
            Entries.Add(new CatalogueEntry(spec, title, displayPath, isBuiltIn: true, version: version, versionDate: versionDate, canOpenInExaminer: ExaminerSupports(spec)));
        }
    }

    private bool ExaminerSupports(string spec) =>
        _examinerLinks?.SupportsSpec(spec) ?? false;

    /// <summary>
    /// Re-evaluates <see cref="CatalogueEntry.CanOpenInExaminer"/> for every
    /// loaded entry against the current examiner settings. Called when the
    /// user toggles the integration or changes the base URL so the per-row
    /// "open in eXaminer" buttons appear/disappear without reloading the list
    /// (issue #442).
    /// </summary>
    public void RefreshExaminerAvailability()
    {
        // Every entry in this view model is a feature catalogue, so
        // eligibility reduces to whether the examiner currently hosts the spec.
        foreach (var entry in Entries)
        {
            entry.CanOpenInExaminer = ExaminerSupports(entry.ProductSpec);
        }
    }

    private CatalogueEntry CreateEntry(string spec, string path, bool isBuiltIn)
    {
        var (name, version, date) = ReadFeatureCatalogueInfo(path);
        // Curated name first; fall back to the catalogue's own declared name
        // (for custom specs outside the bundled set).
        var name2 = Strings.SpecDisplayName(spec) ?? (string.IsNullOrWhiteSpace(name) ? null : name);
        return new CatalogueEntry(spec, ComposeTitle(spec, name2), path, isBuiltIn: isBuiltIn, version: version, versionDate: date, canOpenInExaminer: ExaminerSupports(spec));
    }

    /// <summary>
    /// Composes the primary list line: the spec code followed by the
    /// product name (e.g. "S-101 Electronic Navigational Chart"), or just
    /// the spec code when no name is available.
    /// </summary>
    internal static string ComposeTitle(string spec, string? name) =>
        string.IsNullOrWhiteSpace(name) ? spec : $"{spec} {name}";

    private static (string? Name, string? Version, string? VersionDate) ReadFeatureCatalogueInfo(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var catalogue = FeatureCatalogueReader.Read(stream);
            return (
                string.IsNullOrEmpty(catalogue.Name) ? null : catalogue.Name,
                string.IsNullOrEmpty(catalogue.VersionNumber) ? null : catalogue.VersionNumber,
                string.IsNullOrEmpty(catalogue.VersionDate) ? null : catalogue.VersionDate);
        }
        catch
        {
            return (null, null, null);
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
            Entries.Add(CreateEntry(spec, path, isBuiltIn: false));
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
        Entries.Add(CreateEntry(spec, path, isBuiltIn: false));
    }

    /// <summary>
    /// Adds a built-in catalogue entry that cannot be removed by the user.
    /// Skipped if a user-provided entry already exists for the spec.
    /// </summary>
    public void AddBuiltIn(string spec, string displayPath, string? version = null)
    {
        if (!Entries.Any(e => e.ProductSpec.Equals(spec, StringComparison.OrdinalIgnoreCase)))
        {
            var title = FeatureCataloguesViewModel.ComposeTitle(spec, Strings.SpecDisplayName(spec));
            Entries.Add(new CatalogueEntry(spec, title, displayPath, isBuiltIn: true, version: version));
        }
    }

    private static CatalogueEntry CreateEntry(string spec, string path, bool isBuiltIn)
    {
        // Portrayal catalogues carry no human-readable name, so the curated map
        // is the only name source; the title degrades to the spec code alone.
        var title = FeatureCataloguesViewModel.ComposeTitle(spec, Strings.SpecDisplayName(spec));
        return new CatalogueEntry(spec, title, path, isBuiltIn: isBuiltIn, version: ReadPortrayalCatalogueVersion(path));
    }

    private static string? ReadPortrayalCatalogueVersion(string folderPath)
    {
        try
        {
            var cataloguePath = Path.Combine(folderPath, "portrayal_catalogue.xml");
            if (!File.Exists(cataloguePath)) return null;

            using var stream = File.OpenRead(cataloguePath);
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.Version) ? null : catalogue.Version;
        }
        catch
        {
            return null;
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

internal sealed class CatalogueEntry : ViewModelBase
{
    public string ProductSpec { get; }

    /// <summary>
    /// Human-readable product title shown as the primary line in the list
    /// (e.g. "Electronic Navigational Chart"). Resolved from the curated
    /// spec-name map, the catalogue's declared name, or the spec code.
    /// </summary>
    public string DisplayTitle { get; }

    public string Path { get; }
    public bool IsBuiltIn { get; }
    public string? Version { get; }
    public string? VersionDate { get; }

    /// <summary>
    /// Secondary identity line: version number, version date, and a
    /// "built-in" marker for bundled catalogues. The spec code is omitted
    /// here because it already leads <see cref="DisplayTitle"/>.
    /// </summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(Version))
            {
                parts.Add($"v{Version}");
            }
            if (!string.IsNullOrEmpty(VersionDate))
            {
                parts.Add(VersionDate!);
            }
            if (IsBuiltIn)
            {
                parts.Add(Strings.Catalogue_BuiltInLabel);
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>True when <see cref="Subtitle"/> has content to display.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>
    /// True when the file-system path should be shown. Built-in catalogues
    /// carry no meaningful path (their provenance is shown in the subtitle).
    /// </summary>
    public bool ShowPath => !IsBuiltIn && !string.IsNullOrEmpty(Path);

    /// <summary>
    /// True when this catalogue can be opened in the S-100 Feature Catalogue
    /// eXaminer (issue #442): the examiner integration is enabled and it
    /// hosts a Feature Catalogue for <see cref="ProductSpec"/>. Gates the
    /// per-row "open in eXaminer" button. Always <c>false</c> for portrayal
    /// catalogue entries. Live-refreshed by
    /// <see cref="FeatureCataloguesViewModel.RefreshExaminerAvailability"/>
    /// when the examiner settings change.
    /// </summary>
    public bool CanOpenInExaminer
    {
        get => _canOpenInExaminer;
        internal set => SetProperty(ref _canOpenInExaminer, value);
    }

    private bool _canOpenInExaminer;

    public CatalogueEntry(
        string productSpec,
        string displayTitle,
        string path,
        bool isBuiltIn = false,
        string? version = null,
        string? versionDate = null,
        bool canOpenInExaminer = false)
    {
        ProductSpec = productSpec;
        DisplayTitle = displayTitle;
        Path = path;
        IsBuiltIn = isBuiltIn;
        Version = version;
        VersionDate = versionDate;
        CanOpenInExaminer = canOpenInExaminer;
    }
}
