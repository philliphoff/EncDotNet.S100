using System.Globalization;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A GRIB2 file read into a <see cref="WeatherGridSet"/>: its fields grouped
/// into parameters by code and level, their steps in valid-time order, and
/// where the data comes from. Steps are decoded from the file only when
/// they're needed, so the dataset keeps the <see cref="GribFile"/> open until
/// it's disposed.
/// </summary>
/// <remarks>
/// <para>How the file is read (design note weather-sources.md §3.4 and §7.2):</para>
/// <list type="bullet">
/// <item><description>
/// <b>Files.</b> One file with many messages, or several files appended one
/// after another (what the NOMADS GRIB filter returns for several forecast
/// hours, or <c>cat f000 f003 f006</c>): the order of the messages doesn't
/// matter.
/// </description></item>
/// <item><description>
/// <b>Fields left out</b> (<see cref="IgnoredFields"/>, with why): fields the
/// decoder can't read (<see cref="GribField.UnsupportedReason"/>, for example
/// template 5.42 CCSDS, or a Lambert grid); fields on
/// another grid than the dataset's (the grid of the first readable field the
/// parameter table knows); fields of an older model run; and a field whose
/// parameter, level and valid time repeat an earlier one's.
/// </description></item>
/// <item><description>
/// <b>Parameters.</b> The fields are grouped by GRIB2 code and level, and each
/// group's steps are ordered by valid time (reference time plus forecast
/// time; for a time-interval statistic, template 4.8, such as ECMWF's
/// <c>10fg</c> gust, the end of the interval). A group maps to the canonical
/// table (<see cref="WeatherParameters.FindByGribCode"/>) when its level
/// matches the row's. Each parameter group of the table
/// (<see cref="WeatherParameterDefinition.Group"/>) is read at one level, so a
/// drawn group never mixes levels:
/// <list type="bullet">
/// <item><description>
/// the row's own level when the file has it (10 m for the wind, mean sea
/// level for pressure);
/// </description></item>
/// <item><description>
/// for the wind, the surface when the file has no 10 m wind: GFS-Wave labels
/// the 10 m wind that drives the wave model (its UGRD, VGRD, WIND and WDIR)
/// as at the surface;
/// </description></item>
/// <item><description>
/// for rows with no level of their own, a proper level (surface, mean sea
/// level, a height) before NCEP's numbered partitions (local fixed surface
/// 241, "1 in sequence", "2 in sequence"…), and partition 1 before the others:
/// so GFS-Wave's swell height is its first swell partition.
/// </description></item>
/// </list>
/// Within the chosen level, where a row lists several codes the most preferred
/// one the file has is used. Every other group is kept as an unknown parameter
/// (<c>"grib2:discipline,category,number"</c>): listed, with why in
/// <see cref="UnusedReasons"/>, but not drawn or sampled.
/// </description></item>
/// <item><description>
/// <b>Grid.</b> The fields' <see cref="WeatherNativeGrid"/> (scanning mode as
/// signed increments) normalised by <see cref="WeatherGridLayout"/>: rows
/// flipped to run north, 0 to 360 longitudes moved into −180 to 180. Bitmapped
/// points are <see cref="float.NaN"/>.
/// </description></item>
/// <item><description>
/// <b>Model runs.</b> The run is the fields' reference time. When the fields
/// have several reference times and every one is an analysis (valid at its
/// reference time), the file is an analysis series: <see cref="ReferenceTime"/>
/// is <see langword="null"/> and each analysis is a step. Otherwise the
/// latest run is used and the older runs' fields are left out, so a file of
/// appended runs shows the newest forecast.
/// </description></item>
/// <item><description>
/// <b>Model name.</b> GRIB2 doesn't carry one; see <see cref="Model"/>.
/// </description></item>
/// </list>
/// </remarks>
public sealed class GribWeatherDataset : IDisposable
{
    private GribWeatherDataset(
        GribFile file,
        WeatherGridSet gridSet,
        IReadOnlyList<DateTime> times,
        DateTime? referenceTime,
        IReadOnlyList<GribIgnoredField> ignored,
        IReadOnlyDictionary<WeatherParameter, string> unused)
    {
        File = file;
        GridSet = gridSet;
        Times = times;
        ReferenceTime = referenceTime;
        IgnoredFields = ignored;
        UnusedReasons = unused;
    }

    /// <summary>The open GRIB2 file the steps are decoded from.</summary>
    public GribFile File { get; }

    /// <summary>The grid set: origin, grid and parameters with lazily decoded steps.</summary>
    public WeatherGridSet GridSet { get; }

    /// <summary>
    /// The model name (<see cref="WeatherOrigin.Model"/>). GRIB2 doesn't name
    /// the model, so it's taken from NCEP's generating process number (81, 82
    /// and 96 are the GFS, 11 is GFS-Wave), else from the file name as the
    /// sources name their files (<c>gfs.t00z…</c>, <c>gfswave.t00z…</c>,
    /// <c>filter_gfs_0p25.pl</c>, ECMWF's <c>…-oper-fc</c> for the IFS and
    /// <c>…-wave-fc</c> for ECMWF WAM), else the originating centre
    /// (<c>"ECMWF"</c>). Several models are joined: <c>"GFS + GFS-Wave"</c>.
    /// </summary>
    public string Model => GridSet.Origin.Model;

    /// <summary>The valid times of the parameters the table knows, in time order.</summary>
    public IReadOnlyList<DateTime> Times { get; }

    /// <summary>
    /// The model run's reference time (UTC), or <see langword="null"/> for an
    /// analysis series: fields of several reference times, each valid at its
    /// own (<see cref="WeatherOrigin.RunTime"/> is then the first analysis).
    /// </summary>
    public DateTime? ReferenceTime { get; }

    /// <summary>Fields left out of the grid set, with why, in file order.</summary>
    public IReadOnlyList<GribIgnoredField> IgnoredFields { get; }

    /// <summary>
    /// Why each unknown parameter of <see cref="GridSet"/> isn't used: not in
    /// the parameter table, at a level the table doesn't use, a less preferred
    /// code, or another swell partition.
    /// </summary>
    public IReadOnlyDictionary<WeatherParameter, string> UnusedReasons { get; }

    /// <summary>Opens and reads the GRIB2 file at <paramref name="path"/>.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The dataset; dispose it to close the file.</returns>
    /// <exception cref="GribFormatException">The file isn't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">The file is GRIB edition 1, or none of its fields can be decoded.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid can't be normalised (it crosses the antimeridian).</exception>
    public static GribWeatherDataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(GribFile.Open(path), Path.GetFileName(path));
    }

    /// <summary>
    /// Opens and reads several GRIB2 files as one dataset, as if appended
    /// (one run published a file per forecast hour, #1075): fields are merged
    /// by parameter and level, and each parameter's steps ordered by valid
    /// time, whatever the files' order. Files of several runs keep the latest
    /// run and list the older runs' fields (<see cref="IgnoredFields"/>).
    /// </summary>
    /// <param name="paths">The files; at least one.</param>
    /// <returns>The dataset; dispose it to close the files.</returns>
    /// <exception cref="ArgumentException"><paramref name="paths"/> is empty.</exception>
    /// <exception cref="GribFormatException">A file isn't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">A file is GRIB edition 1, or none of the fields can be decoded.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid can't be normalised (it crosses the antimeridian).</exception>
    public static GribWeatherDataset Open(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Read(GribFile.Open(paths), paths.Count > 0 ? Path.GetFileName(paths[0]) : null);
    }

    /// <summary>
    /// Opens a folder holding one model run (<see cref="GribRunFolder"/>): its
    /// GRIB2 files, in name order, read as one dataset (see <see cref="Open(IReadOnlyList{string})"/>).
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The dataset; dispose it to close the files.</returns>
    /// <exception cref="UnsupportedGribException">The folder holds no GRIB2 file, or none of the fields can be decoded.</exception>
    public static GribWeatherDataset OpenFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var files = GribRunFolder.Files(folder);
        if (files.Count == 0)
            throw new UnsupportedGribException($"The folder {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))} holds no GRIB2 file.");
        return Open(files);
    }

    /// <summary>Opens and reads a GRIB2 file from a seekable stream.</summary>
    /// <param name="stream">The file's bytes; readable and seekable.</param>
    /// <param name="name">The file's name, used for the model name when the file's codes don't give it.</param>
    /// <param name="leaveOpen">Whether disposing the dataset leaves <paramref name="stream"/> open.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="GribFormatException">The bytes aren't valid GRIB2.</exception>
    /// <exception cref="UnsupportedGribException">The data is GRIB edition 1, or none of its fields can be decoded.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid can't be normalised (it crosses the antimeridian).</exception>
    public static GribWeatherDataset Open(Stream stream, string? name = null, bool leaveOpen = false) =>
        Read(GribFile.Open(stream, leaveOpen), name);

    /// <summary>
    /// Reads an open GRIB2 file as weather data. The dataset takes ownership
    /// of <paramref name="file"/>, and disposes it if reading fails.
    /// </summary>
    /// <param name="file">The open file.</param>
    /// <param name="name">The file's name, used for the model name when the file's codes don't give it.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="UnsupportedGribException">None of the file's fields can be decoded.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid can't be normalised (it crosses the antimeridian).</exception>
    public static GribWeatherDataset Read(GribFile file, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            return Build(file, name);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Closes the file.</summary>
    public void Dispose() => File.Dispose();

    private static GribWeatherDataset Build(GribFile file, string? name)
    {
        var ignored = new List<(GribField Field, string Reason)>();

        // 1. Fields the decoder can read.
        var readable = new List<GribField>();
        foreach (var field in file.Fields)
        {
            if (field.UnsupportedReason is { } reason)
                ignored.Add((field, reason));
            else
                readable.Add(field);
        }

        if (readable.Count == 0)
            throw new UnsupportedGribException(NothingReadable(file.Fields));

        // 2. One grid: the first readable field the table knows sets it.
        var gridField = readable.FirstOrDefault(f => WeatherParameters.FindByGribCode(f.Parameter) is not null) ?? readable[0];
        var grid = gridField.ToWeatherNativeGrid();
        var onGrid = new List<GribField>();
        foreach (var field in readable)
        {
            var other = field.ToWeatherNativeGrid();
            if (other == grid)
                onGrid.Add(field);
            else
                ignored.Add((field, $"It's on another grid ({Describe(other)}) than the dataset's ({Describe(grid)})."));
        }

        // 3. One run, or an analysis series.
        var runs = onGrid.Select(f => f.ReferenceTime).Distinct().Order().ToArray();
        DateTime? referenceTime = runs[^1];
        var used = onGrid;
        if (runs.Length > 1)
        {
            if (onGrid.All(f => f.ValidTime == f.ReferenceTime))
            {
                referenceTime = null;
            }
            else
            {
                var latest = runs[^1];
                used = [];
                foreach (var field in onGrid)
                {
                    if (field.ReferenceTime == latest)
                        used.Add(field);
                    else
                        ignored.Add((field, $"It's from an older model run ({Run(field.ReferenceTime)}); the file's latest run, {Run(latest)}, is used."));
                }
            }
        }

        // 4. Group by code and level; one step per valid time.
        var groups = new List<FieldGroup>();
        foreach (var field in used)
        {
            var level = field.ToWeatherLevel();
            var group = groups.Find(g => g.Code == field.Parameter && g.Level == level);
            if (group is null)
            {
                group = new FieldGroup(field.Parameter, level, field);
                groups.Add(group);
            }

            var validTime = field.ValidTime!.Value;
            int earlier = group.Steps.FindIndex(s => s.ValidTime == validTime);
            if (earlier >= 0)
            {
                ignored.Add((field, string.Create(
                    CultureInfo.InvariantCulture,
                    $"Field #{group.Steps[earlier].Field.Index} already gives this parameter at this level and valid time.")));
                continue;
            }

            group.Steps.Add((validTime, field));
        }

        // 5. Map groups to the table: one level per table group, then the most preferred code.
        var unusedReasons = new Dictionary<FieldGroup, string>();
        AssignDefinitions(groups, unusedReasons);

        var origin = new WeatherOrigin(
            GribModelNames.Describe(used, name),
            referenceTime ?? used.Min(f => f.ValidTime!.Value),
            gridField.Identification.CentreName);

        var parameters = new List<WeatherParameter>();
        var unused = new Dictionary<WeatherParameter, string>();
        foreach (var group in groups)
        {
            var steps = group.Steps
                .OrderBy(s => s.ValidTime)
                .Select(s => new WeatherTimeStep(s.ValidTime, file.CreateStepDecoder(s.Field)))
                .ToArray();
            var sourceName = GribShortNames.Get(group.Code) ?? group.Code.ToString();
            if (group.Definition is { } definition)
            {
                parameters.Add(new WeatherParameter(definition, group.Level, steps, sourceName: sourceName));
            }
            else
            {
                var parameter = new WeatherParameter($"grib2:{group.Code}", string.Empty, group.Level, steps, sourceName: sourceName);
                parameters.Add(parameter);
                unused[parameter] = unusedReasons[group];
            }
        }

        var gridSet = new WeatherGridSet(origin, grid, parameters);
        var times = parameters
            .Where(p => p.IsKnown)
            .SelectMany(p => p.Steps)
            .Select(s => s.ValidTime)
            .Distinct()
            .Order()
            .ToArray();
        var ignoredFields = ignored
            .OrderBy(i => i.Field.Index)
            .Select(i => new GribIgnoredField(i.Field, i.Reason))
            .ToArray();
        return new GribWeatherDataset(file, gridSet, times, referenceTime, ignoredFields, unused);
    }

    // Picks, for each table group, the level it's read at, then for each row
    // the most preferred code at that level. Groups not picked keep a null
    // Definition and get a reason.
    private static void AssignDefinitions(List<FieldGroup> groups, Dictionary<FieldGroup, string> reasons)
    {
        var candidates = new List<(FieldGroup Group, WeatherParameterDefinition Definition, int Rank)>();
        foreach (var group in groups)
        {
            var definition = WeatherParameters.FindByGribCode(group.Code);
            if (definition is null)
            {
                reasons[group] = "The weather parameter table doesn't have this parameter.";
                continue;
            }

            int rank = LevelRank(definition, group.Level, group.First);
            if (rank < 0)
            {
                reasons[group] = $"The weather parameter table has '{definition.Id}' only at {definition.Level!.Description}.";
                continue;
            }

            candidates.Add((group, definition, rank));
        }

        foreach (var tableGroup in candidates.Select(c => c.Definition.Group).Distinct().ToArray())
        {
            var members = candidates.Where(c => c.Definition.Group == tableGroup).ToList();
            // Lowest rank first; ties go to the level that appears first in the file.
            var best = members.OrderBy(c => c.Rank).ThenBy(c => c.Group.First.Index).First();
            var level = best.Group.Level;

            foreach (var member in members)
            {
                if (member.Group.Level != level)
                {
                    reasons[member.Group] = $"'{member.Definition.Id}' is read at {level.Description}, the {LevelReason(best.Rank)}.";
                    continue;
                }

                var rivals = members.Where(m => m.Group.Level == level && m.Definition == member.Definition);
                var preferred = rivals
                    .OrderBy(m => IndexOf(m.Definition.GribCodes, m.Group.Code))
                    .ThenBy(m => m.Group.First.Index)
                    .First();
                if (ReferenceEquals(preferred.Group, member.Group))
                {
                    member.Group.Definition = member.Definition;
                }
                else
                {
                    reasons[member.Group] = string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{member.Definition.Id}' is read from {GribShortNames.Describe(preferred.Group.Code)}, which the table prefers.");
                }
            }
        }
    }

    // How well a group's level suits a table row, lower being better: 0 the
    // row's level (or a proper level for a row with none), 1 the surface for
    // the 10 m wind, 11, 12… NCEP partitions 1, 2…, 10 000 any other level,
    // and −1 not at all.
    private static int LevelRank(WeatherParameterDefinition definition, WeatherLevel level, GribField field)
    {
        if (definition.Level is { } wanted)
        {
            if (wanted == level)
                return 0;
            // GFS-Wave's own wind is the 10 m forcing wind, labelled as at the surface.
            return definition.Group == WeatherParameters.WindGroup && level == WeatherLevel.Surface ? 1 : -1;
        }

        if (level.Kind != WeatherLevelKind.Other)
            return 0;
        if (field.Product.FirstSurface is { Type: NcepSequenceSurface, Value: { } sequence } && field.Product.SecondSurface is null &&
            sequence >= 1 && sequence < 1000)
        {
            return 10 + (int)sequence;
        }

        return 10_000;
    }

    private static string LevelReason(int rank) => rank switch
    {
        0 => "level the table uses",
        1 => "surface, because the file has no 10 m wind",
        < 10_000 => "first partition the file has",
        _ => "first level the file has",
    };

    // NCEP local fixed surface type 241: "nth in sequence", used for the
    // WAVEWATCH III swell partitions.
    private const byte NcepSequenceSurface = 241;

    private static int IndexOf(IReadOnlyList<GribParameterCode> codes, GribParameterCode code)
    {
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i] == code)
                return i;
        }

        return codes.Count;
    }

    private static string NothingReadable(IReadOnlyList<GribField> fields)
    {
        var reasons = fields
            .Select(f => f.UnsupportedReason)
            .Distinct()
            .Take(3)
            .ToArray();
        var count = fields.Count == 1 ? "The file's only field" : string.Create(CultureInfo.InvariantCulture, $"None of the file's {fields.Count} fields");
        var verb = fields.Count == 1 ? "can't" : "can";
        return $"{count} {verb} be decoded: {string.Join(" ", reasons)}";
    }

    private static string Describe(WeatherNativeGrid grid) => string.Create(
        CultureInfo.InvariantCulture,
        $"{grid.Columns} × {grid.Rows} points from {grid.FirstLatitude:0.###}°, {grid.FirstLongitude:0.###}° by {grid.LatitudeIncrement:0.####}°, {grid.LongitudeIncrement:0.####}°");

    private static string Run(DateTime time) => time.ToString("yyyy-MM-dd HH'Z'", CultureInfo.InvariantCulture);

    private sealed class FieldGroup(GribParameterCode code, WeatherLevel level, GribField first)
    {
        public GribParameterCode Code { get; } = code;

        public WeatherLevel Level { get; } = level;

        public GribField First { get; } = first;

        public List<(DateTime ValidTime, GribField Field)> Steps { get; } = [];

        public WeatherParameterDefinition? Definition { get; set; }
    }
}
