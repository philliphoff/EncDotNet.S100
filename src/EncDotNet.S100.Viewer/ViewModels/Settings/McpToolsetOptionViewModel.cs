using CommunityToolkit.Mvvm.ComponentModel;
using EncDotNet.S100.Mcp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Integrations: the switch for one MCP toolset (#1608).</summary>
internal sealed partial class McpToolsetOptionViewModel : ObservableObject
{
    private readonly Action _toggled;

    /// <summary>Creates the switch.</summary>
    /// <param name="info">The toolset.</param>
    /// <param name="isEnabled">Whether the server exposes it now.</param>
    /// <param name="toggled">Called after the switch changes.</param>
    public McpToolsetOptionViewModel(McpToolsetInfo info, bool isEnabled, Action toggled)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(toggled);
        Name = info.Name;
        Summary = info.Summary;
        _isEnabled = isEnabled;
        _toggled = toggled;
    }

    /// <summary>The toolset's name, as clients and <c>--mcp-toolsets</c> use it.</summary>
    public string Name { get; }

    /// <summary>What its tools do.</summary>
    public string Summary { get; }

    /// <summary>The switch's automation id.</summary>
    public string AutomationId => "Settings.McpToolset." + Name;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _toggled();
}
