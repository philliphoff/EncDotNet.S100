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
/// The before/after pairs from the rules that branch on it wait for rule
/// probing (R13).
/// </remarks>
internal sealed class DisplaySettingEntryViewModel : ReferenceEntryViewModel
{
    private readonly DisplaySettingEntry _setting;
    private string? _current;

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

    /// <summary>Reads the current value again, after the settings changed.</summary>
    internal void ReadCurrent()
    {
        Current = Context.Settings is { } settings && ReferenceTargets.CurrentValue(settings, _setting.Code) is { } value
            ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_Current, value)
            : null;
        OnPropertyChanged(nameof(CanChange));
    }

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
