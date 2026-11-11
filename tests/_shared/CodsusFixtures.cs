namespace EncDotNet.S100.TestSupport;

/// <summary>
/// The coded surface bulletin fixtures of issues #1295 and #1300 in
/// <c>tests/datasets/CODSUS</c> (copied to <c>TestData/CODSUS</c>). See the
/// README there for where each came from.
/// </summary>
internal static class CodsusFixtures
{
    /// <summary>Real: the high-resolution analysis valid 2026-11-04 00Z, <c>ASUS02 KWBC 040000</c>.</summary>
    public const string HighResolution = "codsus-hr-2026110400.txt";

    /// <summary>Real: the standard analysis of the same time, <c>ASUS01 KWBC 040134</c>.</summary>
    public const string Standard = "codsus-2026110400.txt";

    /// <summary>Real: the short-range forecast bulletin <c>FSUS02 KWBC 050402</c>, 12 h and 24 h sections.</summary>
    public const string Forecast = "codsrp-2026110504.txt";

    /// <summary>Hand-written: intensities, fronts crossing 180°, wraps, an unknown code, valid the year before its issue time.</summary>
    public const string Antimeridian = "synthetic-antimeridian.txt";

    /// <summary>
    /// Hand-written: a 996 hPa Atlantic low with every front type and a trough,
    /// and a 1032 hPa high, at high resolution (issue #1300's portrayal goldens).
    /// </summary>
    public const string AtlanticLow = "synthetic-atlantic-low.txt";

    /// <summary>Every fixture.</summary>
    public static IReadOnlyList<string> All { get; } = [HighResolution, Standard, Forecast, Antimeridian, AtlanticLow];

    /// <summary>The path of a committed fixture in the test output.</summary>
    /// <param name="name">The fixture's file name.</param>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "CODSUS", name);

    /// <summary>Copies a committed fixture to <paramref name="path"/> and returns the path.</summary>
    /// <param name="name">The fixture's file name.</param>
    /// <param name="path">Where to copy it, for example with another extension or none.</param>
    public static string CopyFixture(string name, string path)
    {
        File.Copy(Fixture(name), path, overwrite: true);
        return path;
    }
}
