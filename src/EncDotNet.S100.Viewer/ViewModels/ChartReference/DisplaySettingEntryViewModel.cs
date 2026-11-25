using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A display setting's entry (design note §4.4, C4; R7, #1650): what it
/// does, its default, its current state with <b>Change in Display
/// settings</b>, and the feature types it changes.
/// </summary>
/// <remarks>
/// With probe results (R13a), it also shows before/after pairs from the
/// rules that branch on it, grouped by feature type (R13c, #1722; see
/// <see cref="ReferencePairFinder"/>). Without them, it lists the feature
/// types alone.
/// </remarks>
internal sealed class DisplaySettingEntryViewModel : ReferenceEntryViewModel
{
    private readonly DisplaySettingEntry _setting;
    private string? _current;
    private double? _currentNumber;
    private string? _currentValue;

    /// <summary>Creates the entry view.</summary>
    /// <param name="setting">The display setting.</param>
    /// <param name="context">The host.</param>
    public DisplaySettingEntryViewModel(DisplaySettingEntry setting, ReferenceEntryContext context)
        : base(setting, context, lead: null)
    {
        _setting = setting;
        Default = string.Format(CultureInfo.CurrentCulture, Strings.Reference_Default, DefaultText(setting));
        Changes =
        [
            .. setting.ConditionedFeatureTypes
                .Select(code => LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.FeatureType, code)))
                .OfType<ChartReferenceLinkViewModel>()
                .OrderBy(l => l.Name, StringComparer.CurrentCulture),
        ];
        ChangeCommand = new RelayCommand(() => context.ChangeSetting?.Invoke(setting.Code));
        IsToggle = Toggle(setting.Default) is not null;
        BeforeAfter = PairsOf(setting);
        ReadCurrent();
    }

    /// <inheritdoc />
    public override string CodeLine => _setting.Code;

    /// <inheritdoc />
    public override string KindLabel => Strings.Reference_Kind_DisplaySetting;

    /// <summary>"Default: off".</summary>
    public string Default { get; }

    /// <summary>"Now: on", or <see langword="null"/> when the viewer doesn't set it, so its default holds.</summary>
    public string? Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value))
                OnPropertyChanged(nameof(HasCurrent));
        }
    }

    /// <summary>Whether the viewer sets it.</summary>
    public bool HasCurrent => _current is not null;

    /// <summary>Whether the entry offers Change in Display settings.</summary>
    public bool CanChange => Context.ChangeSetting is not null && _current is not null;

    /// <summary>Opens the settings where it is changed.</summary>
    public ICommand ChangeCommand { get; }

    /// <summary>The feature types whose rules branch on it.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Changes { get; }

    /// <summary>Whether a rule branches on it.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>
    /// The before/after pairs from the rules that branch on it, grouped by
    /// feature type (R13c); empty without probe results.
    /// </summary>
    public IReadOnlyList<ReferencePairGroup> BeforeAfter { get; }

    /// <summary>Whether there are before/after pairs.</summary>
    public bool HasBeforeAfter => BeforeAfter.Count > 0;

    /// <summary>Whether it is on or off, rather than a number.</summary>
    public bool IsToggle { get; }

    /// <summary>"Off, then on", or "Either side of 30 m": how to read the pairs.</summary>
    public string BeforeAfterNote => IsToggle
        ? Strings.Reference_PairsOffOn
        : string.Format(
            CultureInfo.CurrentCulture,
            Strings.Reference_PairsEitherSide,
            _currentValue ?? string.Format(CultureInfo.CurrentCulture, Strings.Reference_Metres, _setting.Default));

    /// <inheritdoc />
    protected override IEnumerable<ReferenceGlyphTile> Tiles =>
        BeforeAfter.SelectMany(g => g.Visible).SelectMany(p => (ReferenceGlyphTile[])[p.Before, p.After]);

    /// <summary>Reads the current value again, after the settings changed.</summary>
    internal void ReadCurrent()
    {
        var mariner = Context.Settings?.BuildMarinerSettings();
        _currentValue = mariner is null ? null : ReferenceTargets.CurrentValue(mariner, _setting.Code);
        Current = _currentValue is { } value
            ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_Current, value)
            : null;
        _currentNumber = mariner is not null && ProbeOutcomes.TryNumber(ReferenceTargets.Value(mariner, _setting.Code), out var n) ? n : null;
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(BeforeAfterNote));
        Relabel();
    }

    // A number's pairs say the values either side of the one set now.
    private void Relabel()
    {
        if (IsToggle || !ProbeOutcomes.TryNumber(_setting.Default, out var probedAt))
            return;
        var now = _currentNumber ?? probedAt;
        foreach (var pair in BeforeAfter.SelectMany(g => g.Pairs))
        {
            if (pair.Offsets is not { } o)
                continue;
            pair.Caption = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Reference_PairCaption,
                o.Attribute,
                (now - (probedAt - o.Below)).ToString("0.##", CultureInfo.CurrentCulture),
                (now + (o.Above - probedAt)).ToString("0.##", CultureInfo.CurrentCulture));
        }
    }

    private List<ReferencePairGroup> PairsOf(DisplaySettingEntry setting)
    {
        if (Index.Probes is not { } probes)
            return [];
        var isToggle = Toggle(setting.Default) is not null;
        var isNumber = ProbeOutcomes.TryNumber(setting.Default, out var probedAt);
        if (!isToggle && !isNumber)
            return [];
        var branching = new HashSet<string>(setting.ConditionedFeatureTypes, StringComparer.OrdinalIgnoreCase);
        var groups = new List<ReferencePairGroup>();
        foreach (var probe in probes.FeatureTypes)
        {
            // A number's pairs are only from the rules that read it: another
            // rule's threshold may happen to equal its default.
            if (!isToggle && !branching.Contains(probe.Code))
                continue;
            if (LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.FeatureType, probe.Code)) is not { } link)
                continue;
            List<ReferencePair> pairs = isToggle
                ?
                [
                    .. ReferencePairFinder.Toggle(probe, setting.Code)
                        .Select(p => new ReferencePair(link.Name, Tile(p.Before), Tile(p.After))),
                ]
                :
                [
                    .. ReferencePairFinder.Either(probe, probedAt)
                        .Select(p => new ReferencePair(link.Name, Tile(p.Before), Tile(p.After))
                        {
                            Offsets = (AttributeName(p.Attribute), p.Below, p.Above),
                        }),
                ];
            if (pairs.Count > 0)
                groups.Add(new ReferencePairGroup(link, pairs, () => Context.Post(DrawMoreTiles)));
        }

        return [.. groups.OrderBy(g => g.Feature.Name, StringComparer.CurrentCulture)];

        ReferenceGlyphTile Tile(string part) => ReferenceGlyphTile.Of(part, Index.Spec, Index, Context.Open);
    }

    private string AttributeName(string code) =>
        Index.TryGet(new ReferenceKey(Index.Spec, ReferenceKind.Attribute, code), out var entry)
            ? ChartReferenceRowViewModel.NameOf(entry)
            : code;

    private static bool? Toggle(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <inheritdoc />
    public override void Close()
    {
        if (Context.Settings is { } settings)
            settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        if (Context.Settings is { } settings)
            settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Context.Post(ReadCurrent);

    // Booleans read "On" and "Off", as the current value does.
    private static string DefaultText(DisplaySettingEntry setting) => setting.Default.Trim().ToLowerInvariant() switch
    {
        "true" => Strings.Reference_On,
        "false" => Strings.Reference_Off,
        _ => setting.Default,
    };
}
