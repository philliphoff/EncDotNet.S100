namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A page of the Settings panel (#845): what <see cref="ISettingsPage.Category"/>
/// names. The category list orders the pages by <see cref="ISettingsPage.Order"/>.
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
