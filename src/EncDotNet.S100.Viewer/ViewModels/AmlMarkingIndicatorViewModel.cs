using System.Globalization;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>One loaded AML set and the markings of its cells.</summary>
/// <param name="SetName">The exchange set's name, or the file name of an AML cell opened on its own.</param>
/// <param name="Markings">Each loaded cell's protective marking (<see langword="null"/> when it has none).</param>
internal sealed record AmlLoadedSet(string SetName, IReadOnlyList<string?> Markings);

/// <summary>A line of the MARKING flyout: one marking and the sets that hold it.</summary>
/// <param name="Marking">The marking, upper case as given.</param>
/// <param name="IsElevated">Whether it ranks above UNCLASSIFIED.</param>
/// <param name="Sets">"AML trial set · 3 cells", one line per set.</param>
internal sealed record AmlMarkingLine(string Marking, bool IsElevated, IReadOnlyList<string> Sets);

/// <summary>
/// The status bar's MARKING pill (#1371, decision 10): the highest protective
/// marking of the AML data loaded, shown after DEPTHS and before MCP.
/// </summary>
/// <remarks>
/// Solid ink with a shield glyph when the highest marking is above
/// UNCLASSIFIED, outlined when everything loaded is UNCLASSIFIED, and gone
/// when no AML is loaded. The marking is never shortened; when the status bar
/// is short of room the pill drops its "MARKING" label instead
/// (<see cref="ShowsLabel"/>). Its flyout lists each marking with its sets.
/// </remarks>
internal sealed class AmlMarkingIndicatorViewModel : ViewModelBase
{
    private bool _isVisible;
    private string? _displayedText;
    private bool _isElevated;
    private bool _showsLabel = true;
    private IReadOnlyList<AmlMarkingLine> _lines = [];

    /// <summary>Whether any AML is loaded: the pill shows.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    /// <summary>The highest marking, with "+N" when others tie with it.</summary>
    public string? DisplayedText
    {
        get => _displayedText;
        private set
        {
            if (SetProperty(ref _displayedText, value))
                OnPropertyChanged(nameof(AccessibleName));
        }
    }

    /// <summary>Whether the highest marking ranks above UNCLASSIFIED: the pill is solid ink.</summary>
    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    /// <summary>Whether the "MARKING" label shows; false when the status bar is short of room.</summary>
    public bool ShowsLabel
    {
        get => _showsLabel;
        set => SetProperty(ref _showsLabel, value);
    }

    /// <summary>"Protective marking RESTRICTED".</summary>
    public string AccessibleName =>
        string.Format(CultureInfo.CurrentCulture, Strings.Aml_Pill_AccessibleNameFormat, _displayedText ?? string.Empty);

    /// <summary>The flyout's lines: each loaded marking, highest first, with its sets.</summary>
    public IReadOnlyList<AmlMarkingLine> Lines
    {
        get => _lines;
        private set => SetProperty(ref _lines, value);
    }

    /// <summary>Shows the markings of the AML sets loaded; none hides the pill.</summary>
    /// <param name="sets">The loaded AML sets.</param>
    public void Update(IReadOnlyList<AmlLoadedSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var loaded = sets.Where(s => s.Markings.Count > 0).ToArray();
        if (loaded.Length == 0)
        {
            IsVisible = false;
            DisplayedText = null;
            IsElevated = false;
            Lines = [];
            return;
        }

        var markings = loaded.SelectMany(s => s.Markings).ToArray();
        var highest = AmlProtectiveMarking.Highest(markings);
        // AML always carries some marking; a cell without one reads as UNCLASSIFIED's outline, not as nothing.
        DisplayedText = highest?.DisplayText ?? AmlProtectiveMarking.Unclassified;
        IsElevated = highest?.IsAboveUnclassified == true;
        IsVisible = true;
        Lines = LinesOf(loaded);
    }

    /// <summary>The flyout lines of <paramref name="sets"/>: each marking, highest first, with the sets holding it.</summary>
    internal static IReadOnlyList<AmlMarkingLine> LinesOf(IReadOnlyList<AmlLoadedSet> sets)
    {
        var byMarking = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            foreach (var group in set.Markings
                .Select(m => AmlProtectiveMarking.Normalize(m) ?? AmlProtectiveMarking.Unclassified)
                .GroupBy(m => m, StringComparer.Ordinal))
            {
                if (!byMarking.TryGetValue(group.Key, out var lines))
                    byMarking[group.Key] = lines = [];
                lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Aml_Pill_SetCellsFormat, set.SetName, CellCount(group.Count())));
            }
        }

        return byMarking
            .OrderByDescending(p => AmlProtectiveMarking.Rank(p.Key))
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new AmlMarkingLine(p.Key, AmlProtectiveMarking.IsAboveUnclassified(p.Key), p.Value))
            .ToArray();
    }

    /// <summary>"1 cell" or "3 cells".</summary>
    internal static string CellCount(int count) => count == 1
        ? Strings.Aml_CellCountOne
        : string.Format(CultureInfo.CurrentCulture, Strings.Aml_CellCountFormat, count);
}
