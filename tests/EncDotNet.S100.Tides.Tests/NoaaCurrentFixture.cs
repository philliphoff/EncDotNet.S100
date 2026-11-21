using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace EncDotNet.S100.Tides.Tests;

/// <summary>
/// Reads the NOAA CO-OPS current fixtures that
/// tools/tide-fixtures/record-noaa-currents.sh records: constituents, offsets
/// and NOAA's own current predictions for 2026. A station is NOAA's id and
/// depth bin, <c>&lt;id&gt;_&lt;bin&gt;</c>.
/// </summary>
internal static class NoaaCurrentFixture
{
    public const int Year = 2026;

    /// <summary>Centimetres per second in a metre per second: NOAA's metric current unit.</summary>
    public const double CentimetresPerMetre = 100;

    /// <summary>The recorded harmonic stations.</summary>
    public static readonly string[] Harmonic = ["PUG1701_33", "SFB1202_17", "LIS1001_13", "MOB1101_2"];

    /// <summary>The recorded subordinate stations and their reference stations.</summary>
    public static readonly (string Id, string Reference)[] Subordinate =
    [
        ("PCT0281_1", "SFB1202_17"),
        ("ACT2401_1", "LIS1001_13"),
    ];

    public static TheoryData<string> HarmonicIds() => [.. Harmonic];

    public static TheoryData<string> SubordinateIds() => [.. Subordinate.Select(s => s.Id)];

    private static string Directory(string id) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "NOAA", "Currents", id);

    /// <summary>A harmonic station from its harcon.json and offsets.json, rectilinear.</summary>
    public static CurrentStation Station(string id, bool isRotary = false)
    {
        using var stream = File.OpenRead(Path.Combine(Directory(id), "harcon.json"));
        using var document = JsonDocument.Parse(stream);
        var rows = document.RootElement.GetProperty("HarmonicConstituents").EnumerateArray().ToList();
        var (flood, ebb) = Directions(id);
        return new CurrentStation(
            id,
            id,
            [
                .. rows.Select(c => new CurrentConstituent(
                    c.GetProperty("constituentName").GetString()!,
                    c.GetProperty("majorAmplitude").GetDouble() / CentimetresPerMetre,
                    c.GetProperty("majorPhaseGMT").GetDouble(),
                    c.GetProperty("minorAmplitude").GetDouble() / CentimetresPerMetre,
                    c.GetProperty("minorPhaseGMT").GetDouble())),
            ],
            rows[0].GetProperty("azi").GetDouble(),
            rows[0].GetProperty("majorMeanSpeed").GetDouble() / CentimetresPerMetre,
            rows[0].GetProperty("minorMeanSpeed").GetDouble() / CentimetresPerMetre,
            flood,
            ebb,
            isRotary);
    }

    /// <summary>NOAA's flood and ebb directions for a station.</summary>
    public static (double Flood, double Ebb) Directions(string id)
    {
        using var offsets = Offsets(id);
        return (offsets.RootElement.GetProperty("meanFloodDir").GetDouble(), offsets.RootElement.GetProperty("meanEbbDir").GetDouble());
    }

    /// <summary>A subordinate station's offsets.</summary>
    public static SubordinateCurrentOffsets SubordinateOffsets(string id)
    {
        using var offsets = Offsets(id);
        var root = offsets.RootElement;
        return new SubordinateCurrentOffsets(
            TimeSpan.FromMinutes(root.GetProperty("sbfTimeAdjMin").GetDouble()),
            TimeSpan.FromMinutes(root.GetProperty("mfcTimeAdjMin").GetDouble()),
            TimeSpan.FromMinutes(root.GetProperty("sbeTimeAdjMin").GetDouble()),
            TimeSpan.FromMinutes(root.GetProperty("mecTimeAdjMin").GetDouble()),
            root.GetProperty("mfcAmpAdj").GetDouble(),
            root.GetProperty("mecAmpAdj").GetDouble());
    }

    private static JsonDocument Offsets(string id)
    {
        using var stream = File.OpenRead(Path.Combine(Directory(id), "offsets.json"));
        return JsonDocument.Parse(stream);
    }

    /// <summary>NOAA's January 6-minute velocities along the major axis, in m/s, from 00:00 UTC on 1 January.</summary>
    public static double[] JanuarySixMinute(string id)
    {
        using var file = File.OpenRead(Path.Combine(Directory(id), "6min.txt.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        Assert.Equal($"# start={Year}-01-01T00:00:00Z step=6", reader.ReadLine());
        var velocities = new List<double>(7_500);
        while (reader.ReadLine() is { } line)
        {
            velocities.Add(double.Parse(line, CultureInfo.InvariantCulture) / CentimetresPerMetre);
        }

        return [.. velocities];
    }

    /// <summary>
    /// NOAA's slacks and turns of the flood and ebb for the year. NOAA
    /// labels a slack only "slack", and every turn "flood" or "ebb" (read
    /// here as a maximum): a slack's kind follows from the turn before it.
    /// </summary>
    public static IReadOnlyList<CurrentEvent> Events(string id)
    {
        var events = new List<CurrentEvent>();
        foreach (var line in File.ReadLines(Path.Combine(Directory(id), "max-slack.csv")).Skip(1))
        {
            var fields = line.Split(',');
            var time = DateTime.SpecifyKind(
                DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);
            var velocity = double.Parse(fields[3], CultureInfo.InvariantCulture) / CentimetresPerMetre;
            var kind = fields[2].Trim() switch
            {
                "flood" => CurrentEventKind.MaximumFlood,
                "ebb" => CurrentEventKind.MaximumEbb,
                "slack" => CurrentEventKind.SlackBeforeFlood,
                var other => throw new InvalidDataException($"Unknown event type '{other}'."),
            };
            events.Add(new CurrentEvent(time, velocity, kind));
        }

        // A slack after a flood is a slack before ebb; the first slack takes
        // its kind from the turn after it.
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Kind != CurrentEventKind.SlackBeforeFlood)
            {
                continue;
            }

            var before = i > 0 ? events[i - 1].Kind : (CurrentEventKind?)null;
            var after = i + 1 < events.Count ? events[i + 1].Kind : (CurrentEventKind?)null;
            var beforeEbb = before == CurrentEventKind.MaximumFlood || before is null && after == CurrentEventKind.MaximumEbb;
            events[i] = events[i] with { Kind = beforeEbb ? CurrentEventKind.SlackBeforeEbb : CurrentEventKind.SlackBeforeFlood };
        }

        return events;
    }
}
