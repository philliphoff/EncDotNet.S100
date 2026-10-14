using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Findings tab in Check passage mode (#905, handoff F1–F5): the
/// checked route's findings grouped MUST REVIEW / CHECK / INFO, projected
/// from <see cref="RouteFindingsService"/>.
/// </summary>
internal sealed class RouteFindingsViewModel : ViewModelBase
{
    private readonly RouteFindingsService _service;

    public RouteFindingsViewModel(RouteFindingsService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        Groups = new ObservableCollection<RouteFindingGroupViewModel>();
        SelectCommand = new RelayCommand<RouteFindingRowViewModel>(row =>
        {
            if (row is not null)
                _service.Select(row.Finding);
        });
        _service.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The non-empty groups, in MUST REVIEW, CHECK, INFO order.</summary>
    public ObservableCollection<RouteFindingGroupViewModel> Groups { get; }

    /// <summary>Selects a finding: zooms the map to it and moves the profile cursor there (F3).</summary>
    public ICommand SelectCommand { get; }

    private int _count;
    /// <summary>How many findings there are; the Findings segment shows it.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Whether there are no findings.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>Raised after <see cref="Count"/> may have changed.</summary>
    public event EventHandler? CountChanged;

    /// <summary>Re-reads the service.</summary>
    public void Refresh()
    {
        var findings = _service.Findings;
        var selectedId = _service.Selected?.Id;
        var groups = findings.GroupBy(f => f.Group).OrderBy(g => g.Key).ToList();

        // Rebuild only what changed so a selection change doesn't recreate rows.
        var same = groups.Count == Groups.Count
            && groups.Zip(Groups).All(p => p.First.Key == p.Second.Group
                && p.First.Select(f => f.Id).SequenceEqual(p.Second.Items.Select(r => r.Finding.Id))
                && p.First.Zip(p.Second.Items).All(q => q.First == q.Second.Finding));
        if (!same)
        {
            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(new RouteFindingGroupViewModel(group.Key, group.Select(f => new RouteFindingRowViewModel(f))));
        }

        foreach (var group in Groups)
        {
            foreach (var row in group.Items)
                row.IsSelected = row.Finding.Id == selectedId;
        }

        Count = findings.Count;
        CountChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A group heading and its rows (F1).</summary>
internal sealed class RouteFindingGroupViewModel
{
    public RouteFindingGroupViewModel(RouteFindingGroup group, IEnumerable<RouteFindingRowViewModel> items)
    {
        Group = group;
        Items = [.. items];
    }

    public RouteFindingGroup Group { get; }

    /// <summary>"MUST REVIEW", "CHECK" or "INFO".</summary>
    public string Label => Group switch
    {
        RouteFindingGroup.MustReview => Strings.Findings_GroupMustReview,
        RouteFindingGroup.Check => Strings.Findings_GroupCheck,
        _ => Strings.Findings_GroupInfo,
    };

    /// <summary>A stable AutomationId suffix: MustReview, Check or Info.</summary>
    public string Key => Group.ToString();

    public IReadOnlyList<RouteFindingRowViewModel> Items { get; }
}

/// <summary>The severity icon a finding row shows (F2).</summary>
internal enum RouteFindingIcon
{
    /// <summary>Octagon, destructive: must review.</summary>
    Octagon,

    /// <summary>Triangle, warning: check.</summary>
    Triangle,

    /// <summary>Waves, muted: a confidence finding.</summary>
    Waves,

    /// <summary>Info, muted.</summary>
    Info,
}

/// <summary>One finding row (F2): icon, title, meta line and tag.</summary>
internal sealed class RouteFindingRowViewModel : ViewModelBase
{
    public RouteFindingRowViewModel(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Finding = finding;
    }

    public RouteFinding Finding { get; }

    public string Title => Finding.Title;

    public string Meta => Finding.Meta;

    /// <summary>The check that produced it: "Clearance", "Route check", "S-124", "Confidence" or "Data".</summary>
    public string Tag => TagOf(Finding.Source);

    /// <summary>The severity icon: a confidence finding shows waves, otherwise its group's icon.</summary>
    public RouteFindingIcon Icon => IconOf(Finding);

    public bool IsOctagon => Icon == RouteFindingIcon.Octagon;

    public bool IsTriangle => Icon == RouteFindingIcon.Triangle;

    public bool IsWaves => Icon == RouteFindingIcon.Waves;

    public bool IsInfo => Icon == RouteFindingIcon.Info;

    /// <summary>The icon's tone (F5: destructive only for must-review findings).</summary>
    public CheckTone Tone => ToneOf(Finding);

    public bool IsDestructive => Tone == CheckTone.Destructive;

    public bool IsWarning => Tone == CheckTone.Warning;

    public bool IsNeutral => Tone == CheckTone.Neutral;

    /// <summary>Whether the finding has a place to zoom to.</summary>
    public bool HasLocation => Finding.Location is not null;

    /// <summary>The row's tooltip: what clicking does.</summary>
    public string Tooltip => HasLocation ? Strings.Tooltip_FindingRow : Strings.Tooltip_FindingRowNoPlace;

    private bool _isSelected;
    /// <summary>Whether this is the selected finding (the selected-row pattern).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    internal static string TagOf(RouteFindingSource source) => source switch
    {
        RouteFindingSource.Clearance => Strings.Findings_TagClearance,
        RouteFindingSource.RouteCheck => Strings.Findings_TagRouteCheck,
        RouteFindingSource.S124 => Strings.Findings_TagS124,
        RouteFindingSource.Confidence => Strings.Findings_TagConfidence,
        _ => Strings.Findings_TagData,
    };

    internal static RouteFindingIcon IconOf(RouteFinding finding) =>
        finding.Source == RouteFindingSource.Confidence ? RouteFindingIcon.Waves
        : finding.Group switch
        {
            RouteFindingGroup.MustReview => RouteFindingIcon.Octagon,
            RouteFindingGroup.Check => RouteFindingIcon.Triangle,
            _ => RouteFindingIcon.Info,
        };

    internal static CheckTone ToneOf(RouteFinding finding) =>
        finding.Group == RouteFindingGroup.MustReview ? CheckTone.Destructive
        : IconOf(finding) == RouteFindingIcon.Triangle ? CheckTone.Warning
        : CheckTone.Neutral;
}
