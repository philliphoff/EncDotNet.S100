namespace EncDotNet.S100.TestSupport;

/// <summary>
/// The CAP fixtures of issue #1069 in <c>tests/datasets/CAP</c> (copied to
/// <c>TestData/CAP</c>), with the facts the tests check. See the README there
/// for where each came from.
/// </summary>
internal static class CapFixtures
{
    /// <summary>
    /// NWS CAP XML, zone codes only: a Gale Warning for PZZ560 (Pigeon Point to
    /// Point Pinos), sent 2026-09-12 08:47 PDT. Its <c>expires</c> (17:00 PDT on
    /// the 12th) is before its <c>onset</c> (15:00 PDT on the 13th); the hazard
    /// ends at the <c>eventEndingTime</c>, 21:00 PDT on the 13th.
    /// </summary>
    public const string GaleWarning = "nws-gale-warning-pzz560.xml";

    /// <summary>NWS CAP XML, zone codes only: a Marine Weather Statement for eleven Chesapeake Bay zones, ANZ530 to ANZ542.</summary>
    public const string MarineWeatherStatement = "nws-marine-weather-statement-anz530.xml";

    /// <summary>
    /// NWS GeoJSON, two alerts: a Special Marine Warning for Delaware Bay with a
    /// polygon (Severe, 2026-08-28 15:12Z to 16:15Z), and a Small Craft Advisory
    /// for PZZ650 with zone codes only.
    /// </summary>
    public const string GeoJson = "nws-alerts-marine-20260828.geojson";

    /// <summary>
    /// The NWS Atom feed, three entries: a Special Marine Warning off the
    /// Florida Keys with a polygon, and two Small Craft Advisories in the Gulf
    /// with zone codes only.
    /// </summary>
    public const string Atom = "nws-alerts-marine-20260603.atom";

    /// <summary>
    /// Hand-written: an Extreme Hazardous Seas Warning (exercise) with a 30 km
    /// <c>circle</c> at 38.5°N 74.5°W, 15:00Z to 18:00Z on 2026-08-28, and a
    /// second area naming ANZ450 and the fictitious ANZ999.
    /// </summary>
    public const string Circle = "hand-written-circle.xml";

    /// <summary>Every fixture.</summary>
    public static IReadOnlyList<string> All { get; } = [GaleWarning, MarineWeatherStatement, GeoJson, Atom, Circle];

    /// <summary>The Gale Warning's onset, 2026-09-13 22:00Z.</summary>
    public static readonly DateTimeOffset GaleOnset = new(2026, 9, 13, 22, 0, 0, TimeSpan.Zero);

    /// <summary>The Gale Warning's hazard end, 2026-09-14 04:00Z.</summary>
    public static readonly DateTimeOffset GaleEnds = new(2026, 9, 14, 4, 0, 0, TimeSpan.Zero);

    /// <summary>The path of a committed fixture in the test output.</summary>
    /// <param name="name">The fixture's file name.</param>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "CAP", name);

    /// <summary>Copies a committed fixture to <paramref name="path"/> and returns the path.</summary>
    /// <param name="name">The fixture's file name.</param>
    /// <param name="path">Where to copy it, for example with another extension or none.</param>
    public static string CopyFixture(string name, string path)
    {
        File.Copy(Fixture(name), path, overwrite: true);
        return path;
    }
}
