using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>One category in the directory's sidebar, with its count (#670, D6/D7).</summary>
internal sealed class CatalogueCategoryViewModel : ViewModelBase
{
    private int _count;
    private bool _hasMore;
    private bool _isOpen;
    private bool _isDimmed;

    public CatalogueCategoryViewModel(KnownCatalogueCategory category, Action select)
    {
        Category = category;
        SelectCommand = new RelayCommand(select);
    }

    /// <summary>The category.</summary>
    public KnownCatalogueCategory Category { get; }

    /// <summary>"Charts", "Depth, tides &amp; currents", …</summary>
    public string Label => LabelOf(Category);

    /// <summary>The label for <paramref name="category"/>.</summary>
    public static string LabelOf(KnownCatalogueCategory category) => category switch
    {
        KnownCatalogueCategory.Depth => Strings.Library_Category_Depth,
        KnownCatalogueCategory.Weather => Strings.Library_Category_Weather,
        KnownCatalogueCategory.Secom => Strings.Library_Category_Secom,
        KnownCatalogueCategory.User => Strings.Library_Category_User,
        _ => Strings.Library_Category_Charts,
    };

    /// <summary>"Charts", "Depth &amp; tides", …: the label in the narrow segmented strip (D2).</summary>
    public string ShortLabel => Category switch
    {
        KnownCatalogueCategory.Depth => Strings.Library_CategoryShort_Depth,
        KnownCatalogueCategory.Weather => Strings.Library_CategoryShort_Weather,
        KnownCatalogueCategory.Secom => Strings.Library_CategoryShort_Secom,
        KnownCatalogueCategory.User => Strings.Library_CategoryShort_User,
        _ => Strings.Library_CategoryShort_Charts,
    };

    /// <summary>How many sources the category lists (how many match while searching).</summary>
    public int Count => _count;

    /// <summary>"17", or "1+" for SECOM services before the registry is listed.</summary>
    public string CountText => _hasMore
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_CategoryCountMoreFormat, _count)
        : _count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The sidebar row's accessible name: "Charts, 17 sources".</summary>
    public string AccessibleName => string.Format(CultureInfo.CurrentCulture, Strings.Library_CategoryAccessibleFormat, Label, CountText);

    /// <summary>True for the open category (not while searching, when every category is shown).</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>True while searching when nothing in the category matches.</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        private set => SetProperty(ref _isDimmed, value);
    }

    /// <summary>Opens the category (and clears the search).</summary>
    public ICommand SelectCommand { get; }

    internal void Update(int count, bool hasMore, bool isOpen, bool isDimmed)
    {
        if (count != _count || hasMore != _hasMore)
        {
            _count = count;
            _hasMore = hasMore;
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(AccessibleName));
        }

        IsOpen = isOpen;
        IsDimmed = isDimmed;
    }
}
