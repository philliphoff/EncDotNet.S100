using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace EncDotNet.S100.Tides.Tests;

/// <summary>
/// Reads the NOAA CO-OPS fixtures that tools/tide-fixtures/record-noaa-tides.sh
/// records: constituents, datums and NOAA's own predictions for 2026.
/// </summary>
internal static class NoaaFixture
{
    public const int Year = 2026;

    /// <summary>The recorded stations, with their names and the zone NOAA's phase_local is referred to.</summary>
    public static readonly (string Id, string Name, double UtcOffsetHours)[] Stations =
    [
        ("9447130", "Seattle", -8),
        ("8443970", "Boston", -5),
        ("8729840", "Pensacola", -6),
        ("9455920", "Anchorage", -9),
        ("9439040", "Astoria", -8),
    ];

    public static TheoryData<string> StationIds()
    {
        var data = new TheoryData<string>();
        foreach (var station in Stations)
        {
            data.Add(station.Id);
        }

        return data;
    }

    private static string Directory(string id) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "NOAA", id);

    /// <summary>One constituent row of harcon.json.</summary>
    public sealed record HarconRow(string Name, double Amplitude, double PhaseGmt, double PhaseLocal, double Speed);

    public static IReadOnlyList<HarconRow> Harcon(string id)
    {
        using var stream = File.OpenRead(Path.Combine(Directory(id), "harcon.json"));
        using var document = JsonDocument.Parse(stream);
        return
        [
            .. document.RootElement.GetProperty("HarmonicConstituents").EnumerateArray().Select(c => new HarconRow(
                c.GetProperty("name").GetString()!,
                c.GetProperty("amplitude").GetDouble(),
                c.GetProperty("phase_GMT").GetDouble(),
                c.GetProperty("phase_local").GetDouble(),
                c.GetProperty("speed").GetDouble())),
        ];
    }

    /// <summary>A datum's value above station datum, from datums.json.</summary>
    public static double Datum(string id, string name)
    {
        using var stream = File.OpenRead(Path.Combine(Directory(id), "datums.json"));
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("datums").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == name)
            .GetProperty("value").GetDouble();
    }

    /// <summary>
    /// The station on MSL, from all its NOAA constituents (Greenwich
    /// phases): 37 at most stations, 120 at Anchorage.
    /// </summary>
    public static TideStation Station(string id) => new(
        id,
        Stations.Single(s => s.Id == id).Name,
        [.. Harcon(id).Select(c => new TideConstituent(c.Name, c.Amplitude, c.PhaseGmt))],
        TideDatum.MeanSeaLevel);

    /// <summary>The station on MLLW: MSL is MSL − MLLW above it.</summary>
    public static TideStation StationOnMllw(string id) =>
        Station(id).WithDatum(new TideDatum("MLLW", Datum(id, "MSL") - Datum(id, "MLLW")));

    /// <summary>NOAA's 6-minute predictions on MSL for the year, from 00:00 UTC on 1 January.</summary>
    public static double[] MslSixMinute(string id)
    {
        using var file = File.OpenRead(Path.Combine(Directory(id), "msl-6min.txt.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var header = reader.ReadLine();
        Assert.Equal($"# start={Year}-01-01T00:00:00Z step=6", header);
        var heights = new List<double>(90_000);
        while (reader.ReadLine() is { } line)
        {
            heights.Add(double.Parse(line, CultureInfo.InvariantCulture));
        }

        return [.. heights];
    }

    /// <summary>NOAA's highs and lows on MSL for the year.</summary>
    public static IReadOnlyList<TideExtreme> MslHighsAndLows(string id) =>
        [
            .. ReadCsv(Path.Combine(Directory(id), "msl-hilo.csv")).Select(f => new TideExtreme(
                f.Time,
                f.Height,
                f.Fields[2].Trim() == "H" ? TideExtremeKind.High : TideExtremeKind.Low)),
        ];

    /// <summary>NOAA's 6-minute predictions on MLLW for January.</summary>
    public static IReadOnlyList<(DateTime Time, double Height)> MllwJanuary(string id) =>
        [.. ReadCsv(Path.Combine(Directory(id), "mllw-6min.csv")).Select(f => (f.Time, f.Height))];

    private static IEnumerable<(DateTime Time, double Height, string[] Fields)> ReadCsv(string path)
    {
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var fields = line.Split(',');
            var time = DateTime.SpecifyKind(
                DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);
            yield return (time, double.Parse(fields[1], CultureInfo.InvariantCulture), fields);
        }
    }
}
