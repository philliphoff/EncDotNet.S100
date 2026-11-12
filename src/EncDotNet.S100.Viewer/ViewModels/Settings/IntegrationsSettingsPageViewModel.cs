using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Integrations: the MCP server and Feature Catalogue eXaminer links (#845, #1398).</summary>
internal sealed class IntegrationsSettingsPageViewModel : SettingsPageViewModel
{
    /// <summary>Creates the page.</summary>
    /// <param name="settings">The settings it shows and edits.</param>
    public IntegrationsSettingsPageViewModel(ViewerSettingsService settings)
        : base(settings)
    {
        ResetMcpPortCommand = new RelayCommand(() => Settings.McpPort = 0);
        ResetExaminerBaseUrlCommand = new RelayCommand(
            () => Settings.ExaminerBaseUrl = ViewerSettings.DefaultS100ExaminerBaseUrl);
    }

    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.Integrations;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_Integrations;

    /// <inheritdoc />
    public override int Order => 50;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.Integrations";

    /// <summary>
    /// Command bound to the "Reset to auto" button. Clears the persisted MCP
    /// port so the next bind picks an ephemeral port (which the host then
    /// persists back).
    /// </summary>
    public ICommand ResetMcpPortCommand { get; }

    /// <summary>
    /// Command bound to the "Reset to default" button next to the eXaminer
    /// base-URL field. Restores <see cref="ViewerSettings.DefaultS100ExaminerBaseUrl"/>.
    /// </summary>
    public ICommand ResetExaminerBaseUrlCommand { get; }

    /// <inheritdoc cref="ViewerSettingsService.McpEnabled" />
    public bool McpEnabled
    {
        get => Settings.McpEnabled;
        set => Settings.McpEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.McpPort" />
    public int McpPort
    {
        get => Settings.McpPort;
        set => Settings.McpPort = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.ExaminerLinksEnabled" />
    public bool ExaminerLinksEnabled
    {
        get => Settings.ExaminerLinksEnabled;
        set => Settings.ExaminerLinksEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.ExaminerBaseUrl" />
    public string ExaminerBaseUrl
    {
        get => Settings.ExaminerBaseUrl;
        set => Settings.ExaminerBaseUrl = value;
    }
}
