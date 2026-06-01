using System;
using System.IO;
using EncDotNet.S100.Viewer;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Verifies that <see cref="StartupSettingsFactory"/> layers CLI
/// overrides (settings isolation, MCP, palette, display category) over
/// the persisted profile without mutating the user's real settings.
/// </summary>
public class StartupSettingsFactoryTests
{
    private static string TempSettingsPath() => Path.Combine(
        Path.GetTempPath(),
        "EncDotNet.S100.Viewer.Tests",
        $"startup-{Guid.NewGuid():N}",
        "settings.json");

    [Fact]
    public void Settings_path_is_used_when_supplied()
    {
        var path = TempSettingsPath();
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings { SettingsPath = path });
        Assert.Equal(path, settings.SettingsFilePath);
        Assert.False(settings.IsReadOnly);
    }

    [Fact]
    public void Ephemeral_marks_settings_read_only_and_save_is_noop()
    {
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings { Ephemeral = true });
        Assert.True(settings.IsReadOnly);

        // Point at a temp file and confirm Save does not write it.
        settings.SettingsFilePath = TempSettingsPath();
        settings.Save();
        Assert.False(File.Exists(settings.SettingsFilePath));
    }

    [Fact]
    public void Mcp_options_enable_server_and_mark_cli_configured()
    {
        var path = TempSettingsPath();
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings
        {
            SettingsPath = path,
            Mcp = true,
            McpPort = 12345,
            McpBind = "127.0.0.1",
            McpPortFile = "/tmp/port.txt",
        });

        Assert.True(settings.McpEnabled);
        Assert.True(settings.McpConfiguredFromCommandLine);
        Assert.Equal(12345, settings.McpPort);
        Assert.Equal("127.0.0.1", settings.McpBindAddress);
        Assert.Equal("/tmp/port.txt", settings.McpPortFilePath);
    }

    [Fact]
    public void No_mcp_options_leave_cli_flag_unset()
    {
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings { SettingsPath = TempSettingsPath() });
        Assert.False(settings.McpConfiguredFromCommandLine);
    }

    [Fact]
    public void Palette_and_display_category_overrides_are_normalized()
    {
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings
        {
            SettingsPath = TempSettingsPath(),
            Palette = "night",
            DisplayCategory = "all",
        });

        Assert.Equal("Night", settings.ColorProfile);
        Assert.Equal("All", settings.EcdisDisplayCategory);
    }

    [Fact]
    public void Null_options_returns_default_profile()
    {
        var settings = StartupSettingsFactory.Create(null);
        Assert.NotNull(settings);
        Assert.False(settings.IsReadOnly);
        Assert.False(settings.McpConfiguredFromCommandLine);
    }
}
