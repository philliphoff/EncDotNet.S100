using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using ShadUI;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Advanced: renderer tuning and maintenance (#845, #1398).</summary>
internal sealed class AdvancedSettingsPageViewModel : SettingsPageViewModel
{
    private readonly ResetApplicationDataWorkflow _reset;
    private readonly DialogManager _dialogs;

    /// <summary>Creates the page.</summary>
    /// <param name="settings">The settings it shows and edits.</param>
    /// <param name="reset">Resets all settings or clears the caches, then restarts.</param>
    /// <param name="dialogs">Shows the confirmations.</param>
    public AdvancedSettingsPageViewModel(ViewerSettingsService settings, ResetApplicationDataWorkflow reset, DialogManager dialogs)
        : base(settings)
    {
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentNullException.ThrowIfNull(dialogs);
        _reset = reset;
        _dialogs = dialogs;
        ResetAllSettingsCommand = new RelayCommand(() => Confirm(ResetApplicationData.ResetAll));
        ClearCachesCommand = new RelayCommand(() => Confirm(ResetApplicationData.ClearCaches));
    }

    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.Advanced;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_Advanced;

    /// <inheritdoc />
    public override int Order => 70;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.Advanced";

    /// <summary>The selectable scene modes for the tiled base plane.</summary>
    public static VectorSceneMode[] AvailableSceneModes { get; } =
        [VectorSceneMode.Tiled, VectorSceneMode.Single];

    /// <summary>Selectable performance profiles for the profile dropdown.</summary>
    public IReadOnlyList<PerformanceProfile> PerformanceProfiles { get; } =
        [PerformanceProfile.Auto, PerformanceProfile.HighEnd, PerformanceProfile.Balanced, PerformanceProfile.LowEnd];

    /// <summary>The Lua engines the page offers.</summary>
    public static Scripting.LuaEngineKind[] AvailableLuaEngines { get; } =
        [Scripting.LuaEngineKind.LuaCSharp, Scripting.LuaEngineKind.MoonSharp];

    /// <summary>True when the tiled base plane is active (tiled scene mode) — gates the tiled knobs.</summary>
    public bool TiledModeActive => Settings.SelectedSceneMode == VectorSceneMode.Tiled;

    /// <summary>
    /// Command bound to the "Reset all settings" button. Shows a
    /// confirmation dialog; on confirm it performs a full clean-slate reset
    /// (settings + crash markers + caches) and restarts the viewer.
    /// </summary>
    public ICommand ResetAllSettingsCommand { get; }

    /// <summary>
    /// Command bound to the "Clear caches" button. Shows a confirmation
    /// dialog; on confirm it deletes the on-disk caches (settings are kept)
    /// and restarts so in-memory caches are dropped too.
    /// </summary>
    public ICommand ClearCachesCommand { get; }

    /// <inheritdoc cref="ViewerSettingsService.SelectedSceneMode" />
    public VectorSceneMode SelectedSceneMode
    {
        get => Settings.SelectedSceneMode;
        set => Settings.SelectedSceneMode = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SceneModeEditable" />
    public bool SceneModeEditable => Settings.SceneModeEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileGutterDip" />
    public double TileGutterDip
    {
        get => Settings.TileGutterDip;
        set => Settings.TileGutterDip = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileGutterDipEditable" />
    public bool TileGutterDipEditable => Settings.TileGutterDipEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileBudgetMb" />
    public double TileBudgetMb
    {
        get => Settings.TileBudgetMb;
        set => Settings.TileBudgetMb = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileBudgetMbEditable" />
    public bool TileBudgetMbEditable => Settings.TileBudgetMbEditable;

    /// <inheritdoc cref="ViewerSettingsService.TilePredictionEnabled" />
    public bool TilePredictionEnabled
    {
        get => Settings.TilePredictionEnabled;
        set => Settings.TilePredictionEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TilePredictionEditable" />
    public bool TilePredictionEditable => Settings.TilePredictionEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileCrossBandPrewarmEnabled" />
    public bool TileCrossBandPrewarmEnabled
    {
        get => Settings.TileCrossBandPrewarmEnabled;
        set => Settings.TileCrossBandPrewarmEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileCrossBandPrewarmEditable" />
    public bool TileCrossBandPrewarmEditable => Settings.TileCrossBandPrewarmEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileMetatileEnabled" />
    public bool TileMetatileEnabled
    {
        get => Settings.TileMetatileEnabled;
        set => Settings.TileMetatileEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileMetatileEditable" />
    public bool TileMetatileEditable => Settings.TileMetatileEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileDiskCacheEnabled" />
    public bool TileDiskCacheEnabled
    {
        get => Settings.TileDiskCacheEnabled;
        set => Settings.TileDiskCacheEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileDiskCacheEditable" />
    public bool TileDiskCacheEditable => Settings.TileDiskCacheEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileDiskMb" />
    public double TileDiskMb
    {
        get => Settings.TileDiskMb;
        set => Settings.TileDiskMb = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileDiskMbEditable" />
    public bool TileDiskMbEditable => Settings.TileDiskMbEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileGpuResidencyEnabled" />
    public bool TileGpuResidencyEnabled
    {
        get => Settings.TileGpuResidencyEnabled;
        set => Settings.TileGpuResidencyEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileGpuResidencyEditable" />
    public bool TileGpuResidencyEditable => Settings.TileGpuResidencyEditable;

    /// <inheritdoc cref="ViewerSettingsService.TileGpuBudgetMb" />
    public double TileGpuBudgetMb
    {
        get => Settings.TileGpuBudgetMb;
        set => Settings.TileGpuBudgetMb = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TileGpuBudgetMbEditable" />
    public bool TileGpuBudgetMbEditable => Settings.TileGpuBudgetMbEditable;

    /// <inheritdoc cref="ViewerSettingsService.SelectedPerformanceProfile" />
    public PerformanceProfile SelectedPerformanceProfile
    {
        get => Settings.SelectedPerformanceProfile;
        set => Settings.SelectedPerformanceProfile = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.PerformanceProfileEditable" />
    public bool PerformanceProfileEditable => Settings.PerformanceProfileEditable;

    /// <inheritdoc cref="ViewerSettingsService.ResolvedProfileLabel" />
    public string ResolvedProfileLabel => Settings.ResolvedProfileLabel;

    /// <inheritdoc cref="ViewerSettingsService.PortrayalPrewarmEnabled" />
    public bool PortrayalPrewarmEnabled
    {
        get => Settings.PortrayalPrewarmEnabled;
        set => Settings.PortrayalPrewarmEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SelectedLuaEngine" />
    public Scripting.LuaEngineKind SelectedLuaEngine
    {
        get => Settings.SelectedLuaEngine;
        set => Settings.SelectedLuaEngine = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.LuaEngineEditable" />
    public bool LuaEngineEditable => Settings.LuaEngineEditable;

    /// <inheritdoc />
    protected override void OnSettingChanged(string? propertyName)
    {
        if (propertyName == nameof(ViewerSettingsService.SelectedSceneMode))
            OnPropertyChanged(nameof(TiledModeActive));
    }

    private void Confirm(ResetApplicationData what)
    {
        var (title, message, button) = what == ResetApplicationData.ResetAll
            ? (Strings.Settings_ResetAll_ConfirmTitle, Strings.Settings_ResetAll_ConfirmMessage, Strings.Settings_ResetAll_ConfirmButton)
            : (Strings.Settings_ClearCaches_ConfirmTitle, Strings.Settings_ClearCaches_ConfirmMessage, Strings.Settings_ClearCaches_ConfirmButton);
        _dialogs
            .CreateDialog(title, message)
            .WithPrimaryButton(button, () => _reset.Run(what), DialogButtonStyle.Destructive)
            .WithCancelButton(Strings.Settings_Cancel)
            .Dismissible()
            .Show();
    }
}
