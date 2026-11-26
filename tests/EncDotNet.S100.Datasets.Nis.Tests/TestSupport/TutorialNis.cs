namespace EncDotNet.S100.Datasets.Nis.Tests.TestSupport;

/// <summary>
/// Esri's sample NIS, which tools/nis-fixtures/fetch-tutorial-nis.sh downloads;
/// it is never committed to the repository.
/// </summary>
internal static class TutorialNis
{
    public static string Path
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("ENC_DOTNET_NIS_FIXTURES");
            if (string.IsNullOrEmpty(root))
                root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "encdotnet-nis-fixtures");
            return System.IO.Path.Combine(root, "TUTORIAL_NIS.gdb");
        }
    }

    public static void SkipUnlessPresent()
        => Assert.SkipUnless(
            Directory.Exists(Path),
            $"Esri's sample NIS isn't at {Path}. Run tools/nis-fixtures/fetch-tutorial-nis.sh to download it.");
}
