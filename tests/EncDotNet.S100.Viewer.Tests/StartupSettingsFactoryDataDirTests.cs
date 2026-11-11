namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the data-dir / <c>--settings</c> interaction in
/// <see cref="StartupSettingsFactory"/> and the clean-slate
/// <see cref="ViewerSettings.ResetForRestart"/> behaviour.
/// </summary>
public class StartupSettingsFactoryDataDirTests
{
    [Fact]
    public void Create_UsesSettingsFileFromDataPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        var paths = new ViewerDataPaths(dir);

        var settings = StartupSettingsFactory.Create(options: null, paths);

        Assert.Equal(Path.Combine(paths.BaseDirectory!, "settings.json"), settings.SettingsFilePath);
    }

    [Fact]
    public void Create_SettingsOverrideWinsOverDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(Path.GetTempPath(), "custom-" + Guid.NewGuid().ToString("N") + ".json");
        var options = new ViewerCommandSettings { DataDir = dir, SettingsPath = settingsFile };

        var settings = StartupSettingsFactory.Create(options);

        Assert.Equal(Path.GetFullPath(settingsFile), settings.SettingsFilePath);
    }

    [Fact]
    public void Create_EphemeralLoadsReadOnly()
    {
        var settings = StartupSettingsFactory.Create(new ViewerCommandSettings { Ephemeral = true });

        Assert.True(settings.IsReadOnly);
    }

    [Fact]
    public void ResetForRestart_DeletesFileAndSuppressesFurtherSaves()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "{}");

        try
        {
            var settings = ViewerSettings.Load(file);

            settings.ResetForRestart();

            Assert.True(settings.IsReadOnly);
            Assert.False(File.Exists(file));

            // A subsequent save must be a no-op (read-only) so the deleted
            // file is not resurrected during shutdown.
            settings.Save();
            Assert.False(File.Exists(file));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
