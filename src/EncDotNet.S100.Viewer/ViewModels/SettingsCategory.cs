namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A page of the Settings panel, in the order the category list shows them
/// (#845). <see cref="SettingsViewModel.SelectedCategoryIndex"/> is this
/// value as an index, so the members' order must match the tabs in
/// <c>SettingsView.axaml</c>.
/// </summary>
internal enum SettingsCategory
{
    /// <summary>Units and time format.</summary>
    General,

    /// <summary>Accent colour, chrome theme, colour profile, symbol and text scale.</summary>
    Appearance,

    /// <summary>Basemap, scale indicators and mariner settings.</summary>
    Chart,

    /// <summary>Own vessel and the AIS overlay.</summary>
    Vessels,

    /// <summary>The MCP server and Feature Catalogue eXaminer links.</summary>
    Integrations,

    /// <summary>MCP identities, trusted authorities and system IDs (#845).</summary>
    KeysAndCertificates,

    /// <summary>Renderer tuning and maintenance.</summary>
    Advanced,
}
