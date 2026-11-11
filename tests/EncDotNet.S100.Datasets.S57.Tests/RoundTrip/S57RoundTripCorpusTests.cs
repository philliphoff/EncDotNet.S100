using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests.RoundTrip;

/// <summary>
/// Opt-in round trip over a corpus of S-57 cells: S-57 → S-101 → S-57 model →
/// ISO 8211 bytes (<see cref="S57DocumentWriter"/>) → S-57 model read back
/// (<see cref="S57DocumentReader"/>), the result compared with its source (see
/// <see cref="S57RoundTripComparer"/>). The headline figure is the number of
/// unexplained differences, each most likely a forward-mapping bug.
/// </summary>
/// <remarks>
/// <para>
/// Set <c>ENCDOTNET_S57_CORPUS</c> to a folder searched recursively for
/// <c>.000</c> base cells (for example a NOAA <c>ENC_ROOT</c>). Optional:
/// <c>ENCDOTNET_S57_CORPUS_LIMIT</c> caps the number of cells, and
/// <c>ENCDOTNET_ROUNDTRIP_REPORT</c> names the JSON report file (default
/// <c>s57-roundtrip-report.json</c> in the temp folder). Skipped otherwise, so
/// CI never depends on real ENC data. Inland (S-401) cells are skipped.
/// </para>
/// <para>
/// The report's <c>WriteReadBack</c> section covers the encoding step: cells
/// the writer rejected, and fields that differ between the translated model
/// and the model read back (<see cref="S57ModelDiff"/>). A cell the writer
/// rejects is still compared with its source, using the translated model.
/// </para>
/// </remarks>
public class S57RoundTripCorpusTests
{
    private const string CorpusVariable = "ENCDOTNET_S57_CORPUS";
    private const string LimitVariable = "ENCDOTNET_S57_CORPUS_LIMIT";
    private const string ReportVariable = "ENCDOTNET_ROUNDTRIP_REPORT";
    private const int SamplesPerBucket = 5;

    private readonly ITestOutputHelper _output;

    public S57RoundTripCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RoundTrip_Corpus_ReportsDifferences()
    {
        var root = Environment.GetEnvironmentVariable(CorpusVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(root), $"{CorpusVariable} not set.");
        Assert.SkipUnless(Directory.Exists(root), $"{root} does not exist.");

        var cells = Directory.EnumerateFiles(root!, "*.000", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        if (int.TryParse(Environment.GetEnvironmentVariable(LimitVariable), out var limit) && limit > 0)
            cells = cells.Take(limit).ToList();
        Assert.SkipWhen(cells.Count == 0, $"No .000 cells under {root}.");

        var comparer = new S57RoundTripComparer();
        var forward = new S57ToS101Translator();
        var reverse = new S101ToS57Translator();
        var report = new Report();
        var reverseDiagnostics = new S101ToS57TranslationDiagnostics();
        var forwardDiagnostics = new S57TranslationDiagnostics();

        foreach (var path in cells)
        {
            S57Dataset dataset;
            try
            {
                dataset = S57Dataset.Open(path);
            }
            catch (Exception ex)
            {
                report.Failures.Add(new CellFailure(Path.GetFileName(path), "read: " + ex.Message));
                continue;
            }
            if (dataset.TranslationTarget != S57TranslationTarget.S101)
            {
                report.CellsSkipped++;
                continue;
            }

            try
            {
                var s101 = forward.Translate(dataset.Document, forwardDiagnostics);
                var back = reverse.Translate(s101, reverseDiagnostics);
                var result = report.WriteReadBack.WriteAndReadBack(Path.GetFileName(path), back) ?? back;
                var differences = comparer.Compare(dataset.Document, s101, result);
                report.Add(Path.GetFileName(path), dataset.Document.FeatureRecords.Count, differences);
            }
            catch (Exception ex)
            {
                report.Failures.Add(new CellFailure(Path.GetFileName(path), $"{ex.GetType().Name}: {ex.Message} {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"));
            }
        }

        report.Reverse = ReverseSummary.From(reverseDiagnostics);
        report.DataCoverageCuts = forwardDiagnostics.DataCoverageCuts;
        report.DataCoverageNotCut = forwardDiagnostics.DataCoverageNotCut;
        var reportPath = Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "s57-roundtrip-report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));

        _output.WriteLine($"Cells: {report.CellsCompared} compared, {report.CellsSkipped} skipped, {report.Failures.Count} failed.");
        _output.WriteLine($"Source objects: {report.SourceFeatures}. Differences: {report.Totals.Sum(t => t.Value)}.");
        foreach (var (cls, count) in report.Totals)
            _output.WriteLine($"  {cls}: {count}");
        var written = report.WriteReadBack;
        _output.WriteLine($"Written: {written.CellsWritten}, read back unchanged: {written.CellsReadBackUnchanged}, write failures: {written.WriteFailures.Count}.");
        _output.WriteLine($"Data Coverage cut by M_CSCL: {report.DataCoverageCuts}, not cut: {report.DataCoverageNotCut}.");
        _output.WriteLine($"Report: {reportPath}");

        Assert.Empty(report.Failures);
        Assert.Empty(report.WriteReadBack.WriteFailures);
        Assert.Empty(report.WriteReadBack.ReadFailures);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record CellFailure(string Cell, string Error);

    private sealed record Bucket
    {
        public required string ObjectClass { get; init; }
        public required string Attribute { get; init; }
        public required string Kind { get; init; }
        public required RoundTripClass Class { get; init; }
        public required string Reason { get; init; }
        public string? Issue { get; init; }
        public int Count { get; set; }
        public int Cells { get; set; }
        public List<string> Samples { get; } = [];
    }

    private sealed record FieldBucket(string Field)
    {
        public int Count { get; set; }
        public int Cells { get; set; }
        public List<string> Samples { get; } = [];
    }

    // The write → read-back step: S57DocumentWriter on the translated model,
    // then S57DocumentReader on the bytes.
    private sealed class WriteReadBackSummary
    {
        private readonly Dictionary<string, FieldBucket> _fields = new(StringComparer.Ordinal);

        public int CellsWritten { get; private set; }
        public int CellsReadBackUnchanged { get; private set; }
        public long BytesWritten { get; private set; }
        public List<CellFailure> WriteFailures { get; } = [];
        public List<CellFailure> ReadFailures { get; } = [];

        /// <summary>Fields that differ after the read back, largest first.</summary>
        public IEnumerable<FieldBucket> ReadBackDifferences => _fields.Values.OrderByDescending(b => b.Count).ThenBy(b => b.Field, StringComparer.Ordinal);

        // Returns the model read back, or null when the cell didn't write or read.
        public S57Document? WriteAndReadBack(string cell, S57Document translated)
        {
            byte[] bytes;
            try
            {
                bytes = S57DocumentWriter.Write(translated);
            }
            catch (ArgumentException ex)
            {
                WriteFailures.Add(new CellFailure(cell, ex.Message));
                return null;
            }
            CellsWritten++;
            BytesWritten += bytes.Length;

            S57Document readBack;
            try
            {
                readBack = S57DocumentReader.Read(bytes);
            }
            catch (Exception ex)
            {
                ReadFailures.Add(new CellFailure(cell, $"{ex.GetType().Name}: {ex.Message}"));
                return null;
            }

            var differences = S57ModelDiff.Compare(translated, readBack);
            if (differences.Count == 0) CellsReadBackUnchanged++;
            foreach (var group in differences.GroupBy(d => d.Field))
            {
                if (!_fields.TryGetValue(group.Key, out var bucket))
                    _fields[group.Key] = bucket = new FieldBucket(group.Key);
                bucket.Cells++;
                foreach (var d in group)
                {
                    bucket.Count++;
                    if (bucket.Samples.Count < SamplesPerBucket)
                        bucket.Samples.Add($"{cell} {d.Detail}");
                }
            }
            return readBack;
        }
    }

    private sealed record CellSummary(string Cell, int Features, int Differences, int Unexplained, int ReverseBugs);

    private sealed record ReverseSummary(
        int FeaturesRead,
        int FeaturesWritten,
        Dictionary<string, int> DroppedFeatures,
        Dictionary<string, int> Restructured,
        Dictionary<string, int> UnmappedFeatureClasses,
        Dictionary<string, int> DroppedAttributes,
        Dictionary<string, int> UnmappedAttributes,
        Dictionary<string, int> FoldedIntoInform,
        Dictionary<string, int> TruncatedToFirst,
        Dictionary<string, int> MandatoryAttributesNulled,
        Dictionary<string, int> FeaturesSynthesised,
        Dictionary<string, int> MovedToNational,
        Dictionary<string, int> UnencodableText,
        int InformOverLength,
        int NonBooleanValues,
        Dictionary<string, int> PrimitivesChanged,
        Dictionary<string, int> SkinOfTheEarthCuts,
        Dictionary<string, int> DroppedInformationTypes)
    {
        public static ReverseSummary From(S101ToS57TranslationDiagnostics d) => new(
            d.FeaturesRead,
            d.FeaturesWritten,
            Sorted(d.DroppedFeatures.ToDictionary(kv => $"{kv.Key.S101Code} (§{kv.Key.Clause})", kv => kv.Value)),
            Sorted(d.Restructured.ToDictionary(kv => $"{kv.Key.S101Code} (§{kv.Key.Clause})", kv => kv.Value)),
            Sorted(d.UnmappedFeatureClasses.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.DroppedAttributes.ToDictionary(kv => $"{kv.Key.S101Code} (§{kv.Key.Clause})", kv => kv.Value)),
            Sorted(d.UnmappedAttributes.ToDictionary(kv => $"{kv.Key.S101FeatureCode}.{kv.Key.S101AttributeCode}", kv => kv.Value)),
            Sorted(d.FoldedIntoInform.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.TruncatedToFirst.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.MandatoryAttributesNulled.ToDictionary(kv => $"{kv.Key.ObjectClass}.{kv.Key.Attribute}", kv => kv.Value)),
            Sorted(d.FeaturesSynthesised.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.MovedToNational.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.UnencodableText.ToDictionary(kv => kv.Key, kv => kv.Value)),
            d.InformOverLength,
            d.NonBooleanValues,
            Sorted(d.PrimitivesChanged.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.SkinOfTheEarthCuts.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Sorted(d.DroppedInformationTypes.ToDictionary(kv => $"{kv.Key.S101Code} (§{kv.Key.Clause})", kv => kv.Value)));

        private static Dictionary<string, int> Sorted(Dictionary<string, int> map)
            => map.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary();
    }

    private sealed class Report
    {
        private readonly Dictionary<(string, string, string, RoundTripClass, string), Bucket> _buckets = new();
        private readonly Dictionary<(string, string, string, RoundTripClass, string), HashSet<string>> _bucketCells = new();

        public int CellsCompared { get; private set; }
        public int CellsSkipped { get; set; }
        public int SourceFeatures { get; private set; }
        public Dictionary<RoundTripClass, int> Totals { get; } = Enum.GetValues<RoundTripClass>().ToDictionary(c => c, _ => 0);
        public List<CellFailure> Failures { get; } = [];
        public ReverseSummary? Reverse { get; set; }
        public int DataCoverageCuts { get; set; }
        public int DataCoverageNotCut { get; set; }
        public WriteReadBackSummary WriteReadBack { get; } = new();

        /// <summary>The unexplained differences and reverse bugs, largest first.</summary>
        public IEnumerable<Bucket> Unexplained => Buckets.Where(b => b.Class is RoundTripClass.Unexplained or RoundTripClass.ReverseBug);

        /// <summary>Every difference bucket, largest first.</summary>
        public IEnumerable<Bucket> Buckets => _buckets.Values.OrderByDescending(b => b.Count).ThenBy(b => b.ObjectClass, StringComparer.Ordinal);

        public List<CellSummary> Cells { get; } = [];

        public void Add(string cell, int features, List<RoundTripDifference> differences)
        {
            CellsCompared++;
            SourceFeatures += features;
            foreach (var d in differences)
            {
                Totals[d.Class]++;
                var key = (d.ObjectClass, d.Attribute, d.Kind, d.Class, d.Reason);
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket { ObjectClass = d.ObjectClass, Attribute = d.Attribute, Kind = d.Kind, Class = d.Class, Reason = d.Reason, Issue = d.Issue };
                    _buckets[key] = bucket;
                    _bucketCells[key] = [];
                }
                bucket.Count++;
                if (_bucketCells[key].Add(cell)) bucket.Cells++;
                if (bucket.Samples.Count < SamplesPerBucket && (d.Source is not null || d.Result is not null))
                    bucket.Samples.Add($"{cell}: {d.Source ?? "∅"} → {d.Result ?? "∅"}");
            }
            Cells.Add(new CellSummary(cell, features, differences.Count,
                differences.Count(d => d.Class == RoundTripClass.Unexplained),
                differences.Count(d => d.Class == RoundTripClass.ReverseBug)));
        }
    }
}
