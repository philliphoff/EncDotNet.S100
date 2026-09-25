using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the "Add Online Catalogue" directory (issue #670): lists the
/// curated known chart catalogues by region with what each provides, and hands
/// the chosen one on to the Add to Library dialog.
/// </summary>
internal sealed class CatalogueDirectoryDialogViewModel : ViewModelBase
{
    private CatalogueEntryViewModel? _selected;

    public CatalogueDirectoryDialogViewModel(IReadOnlyList<KnownCatalogueSource> sources, Action<Uri>? openUrl = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        Entries = sources
            .OrderBy(s => string.Join('/', s.Region), StringComparer.CurrentCulture)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new CatalogueEntryViewModel(s, openUrl))
            .ToArray();
        _selected = Entries.FirstOrDefault();

        NextCommand = new RelayCommand(() => Chosen?.Invoke(this, _selected!.Source), () => _selected is not null);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised with the catalogue the user chose.</summary>
    public event EventHandler<KnownCatalogueSource>? Chosen;

    /// <summary>Raised when the user cancels.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The catalogues, sorted by region then name.</summary>
    public IReadOnlyList<CatalogueEntryViewModel> Entries { get; }

    /// <summary>The highlighted catalogue.</summary>
    public CatalogueEntryViewModel? SelectedEntry
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Continues with the selected catalogue.</summary>
    public ICommand NextCommand { get; }

    public ICommand CancelCommand { get; }
}

/// <summary>One catalogue in the directory, with display text for its quality chips.</summary>
internal sealed class CatalogueEntryViewModel : ViewModelBase
{
    public CatalogueEntryViewModel(KnownCatalogueSource source, Action<Uri>? openUrl)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        OpenHomepageCommand = new RelayCommand(
            () => openUrl?.Invoke(source.Homepage!),
            () => openUrl is not null && source.Homepage is not null);
    }

    /// <summary>The known source.</summary>
    public KnownCatalogueSource Source { get; }

    public string Name => Source.Name;

    /// <summary>"U.S. Army Corps of Engineers · North America › United States".</summary>
    public string ProviderAndRegion =>
        Source.Region.Count == 0 ? Source.Provider : $"{Source.Provider} · {string.Join(" › ", Source.Region)}";

    public string? Note => Source.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Source.Note);

    /// <summary>What the map can show before anything is downloaded.</summary>
    public string CoverageChip => Source.Coverage switch
    {
        KnownCatalogueCoverage.Polygons => Strings.Library_Chip_CoveragePolygons,
        KnownCatalogueCoverage.BoundingBoxes => Strings.Library_Chip_CoverageBoxes,
        _ => Strings.Library_Chip_CoverageNone,
    };

    /// <summary>True when the coverage chip should read as a limitation.</summary>
    public bool IsCoverageLimited => Source.Coverage == KnownCatalogueCoverage.None;

    public string EditionsChip => Source.Editions ? Strings.Library_Chip_Editions : Strings.Library_Chip_NoEditions;

    public bool IsEditionsLimited => !Source.Editions;

    public string SizesChip => Source.Sizes ? Strings.Library_Chip_Sizes : Strings.Library_Chip_NoSizes;

    public bool IsSizesLimited => !Source.Sizes;

    public string CatalogUrl => Source.CatalogUri.AbsoluteUri;

    /// <summary>Opens the provider's page (terms and notices).</summary>
    public ICommand OpenHomepageCommand { get; }

    public bool HasHomepage => Source.Homepage is not null;
}
