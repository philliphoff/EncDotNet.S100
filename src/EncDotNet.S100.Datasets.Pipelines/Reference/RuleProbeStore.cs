using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Reads and writes <see cref="RuleProbeSet"/>s: the file committed next to
/// a bundled portrayal catalogue, and a disk cache for catalogues the user
/// adds or changes (symbol dictionary design note §7, R13a).
/// </summary>
/// <remarks>
/// <para>
/// The stored form is UTF-8 JSON, one feature type per line so a re-probe
/// diffs by feature type:
/// </para>
/// <code>
/// {"format":1,"spec":"S-101","fc":"&lt;hash&gt;","pc":"&lt;hash&gt;","cap":512,"featureTypes":[
/// {"code":"LateralBeacon","partial":true,
///  "inputs":[["primitive","PrimitiveType",["Point"]],["attribute","colour",["3,4,3","3",null]],["context","SimplifiedSymbols",["true","false"]]],
///  "lines":[["LateralBeacon",23],["LateralBeacon",24]],
///  "outcomes":[{"parts":["symbol:BCNLAT21"],"vg":[27020],"cond":[0,1]}],
///  "variants":[0,0,1]}
/// ]}
/// </code>
/// <para>
/// The keys follow <see cref="RuleProbeSet"/>, <see cref="FeatureTypeProbe"/>,
/// <see cref="ProbeInput"/> (kind, code, values), <see cref="ProbeConditionLine"/>
/// (file, line) and <see cref="ProbeOutcome"/>; <c>"error":true</c> marks
/// <see cref="ProbeOutcome.IsError"/>. Writing the same set gives the same
/// bytes. A file whose name ends in <c>.gz</c> is gzip-compressed.
/// </para>
/// </remarks>
public static class RuleProbeStore
{
    /// <summary>The file name of the probe results committed next to a bundled portrayal catalogue.</summary>
    public const string FileName = "rule-probes.json";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>Writes <paramref name="set"/> to <paramref name="stream"/>.</summary>
    /// <param name="set">The probe results.</param>
    /// <param name="stream">The stream to write to; left open.</param>
    public static void Write(RuleProbeSet set, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(stream);

        var newline = "\n"u8;
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", set.FormatVersion);
            writer.WriteString("spec", set.Spec);
            writer.WriteString("fc", set.FeatureCatalogueHash);
            writer.WriteString("pc", set.PortrayalCatalogueHash);
            writer.WriteNumber("cap", set.Cap);
            writer.WritePropertyName("featureTypes");
            writer.WriteStartArray();
            writer.Flush();
        }

        stream.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
        for (var i = 0; i < set.FeatureTypes.Count; i++)
        {
            stream.Write(newline);
            buffer.SetLength(0);
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                WriteFeatureType(writer, set.FeatureTypes[i]);
            }

            stream.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (i < set.FeatureTypes.Count - 1) stream.Write(","u8);
        }

        stream.Write(newline);
        stream.Write("]}"u8);
        stream.Write(newline);
    }

    /// <summary>Writes <paramref name="set"/> to a file, replacing it; gzip-compressed when the name ends in <c>.gz</c>.</summary>
    /// <param name="set">The probe results.</param>
    /// <param name="path">The file.</param>
    public static void WriteFile(RuleProbeSet set, string path)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Written beside the target and moved over it, so a reader never sees half a file.
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        {
            if (IsCompressed(path))
            {
                using var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true);
                Write(set, gzip);
            }
            else
            {
                Write(set, file);
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads probe results written by <see cref="Write"/>.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The probe results.</returns>
    /// <exception cref="InvalidDataException">The stream isn't probe results this version can read.</exception>
    public static RuleProbeSet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The probe results aren't valid JSON.", ex);
        }

        using (document)
        {
            try
            {
                return ReadSet(document.RootElement);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
            {
                throw new InvalidDataException("The probe results are malformed.", ex);
            }
        }
    }

    /// <summary>Reads probe results from a file; gzip-compressed when the name ends in <c>.gz</c>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The probe results.</returns>
    public static RuleProbeSet ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var file = File.OpenRead(path);
        if (!IsCompressed(path)) return Read(file);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return Read(gzip);
    }

    /// <summary>
    /// Reads the probe results committed next to <paramref name="spec"/>'s
    /// bundled portrayal catalogue, when they describe these catalogues.
    /// </summary>
    /// <param name="spec">The product specification, such as <c>S-101</c>.</param>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The results, or <see langword="null"/> when none are bundled, they
    /// can't be read, or their hashes don't match (a user-added or changed
    /// catalogue).
    /// </returns>
    public static async Task<RuleProbeSet?> TryLoadBundledAsync(
        string spec, string? featureCatalogueHash, string? portrayalCatalogueHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        if (featureCatalogueHash is null || portrayalCatalogueHash is null || !Specification.HasPortrayalCatalogue(spec))
            return null;

        try
        {
            using var source = Specification.CreatePortrayalCatalogueSource(spec);
            await using var stream = await source.OpenAsync(FileName, cancellationToken).ConfigureAwait(false);
            var set = Read(stream);
            return set.Matches(featureCatalogueHash, portrayalCatalogueHash) ? set : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>The disk cache file for one pair of catalogues.</summary>
    /// <param name="directory">The cache directory.</param>
    /// <param name="spec">The product specification, such as <c>S-101</c>.</param>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash.</param>
    /// <returns>The file path; it may not exist.</returns>
    public static string CachePath(string directory, string spec, string featureCatalogueHash, string portrayalCatalogueHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(spec);
        ArgumentException.ThrowIfNullOrEmpty(featureCatalogueHash);
        ArgumentException.ThrowIfNullOrEmpty(portrayalCatalogueHash);
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{spec.Replace("-", string.Empty, StringComparison.Ordinal)}-{Short(featureCatalogueHash)}-{Short(portrayalCatalogueHash)}-v{RuleProbeSet.CurrentFormatVersion}.json.gz");
        return Path.Combine(directory, name);

        static string Short(string hash) => hash.Length > 16 ? hash[..16] : hash;
    }

    /// <summary>Reads cached probe results for these catalogues.</summary>
    /// <param name="directory">The cache directory.</param>
    /// <param name="spec">The product specification.</param>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash.</param>
    /// <returns>The results, or <see langword="null"/> when none are cached or the cached file can't be read.</returns>
    public static RuleProbeSet? TryLoadCached(
        string directory, string spec, string featureCatalogueHash, string portrayalCatalogueHash)
    {
        var path = CachePath(directory, spec, featureCatalogueHash, portrayalCatalogueHash);
        if (!File.Exists(path)) return null;
        try
        {
            var set = ReadFile(path);
            return set.Matches(featureCatalogueHash, portrayalCatalogueHash) ? set : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes probe results to the disk cache under their catalogue hashes.</summary>
    /// <param name="directory">The cache directory; created when missing.</param>
    /// <param name="set">The probe results.</param>
    /// <returns>The file written.</returns>
    public static string SaveCached(string directory, RuleProbeSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var path = CachePath(directory, set.Spec, set.FeatureCatalogueHash, set.PortrayalCatalogueHash);
        WriteFile(set, path);
        return path;
    }

    /// <summary>
    /// Finds probe results for a pair of catalogues: those committed with a
    /// bundled catalogue when the hashes match, else those cached on disk,
    /// else runs <paramref name="probe"/> and caches what it returns.
    /// </summary>
    /// <param name="spec">The product specification, such as <c>S-101</c>.</param>
    /// <param name="featureCatalogueHash">The feature catalogue's content hash.</param>
    /// <param name="portrayalCatalogueHash">The portrayal catalogue's content hash.</param>
    /// <param name="cacheDirectory">The disk cache, or <see langword="null"/> for none.</param>
    /// <param name="probe">Probes the rules when nothing is found, or <see langword="null"/> to only look.</param>
    /// <param name="cancellationToken">Cancels the lookup and the probe.</param>
    /// <returns>The results, or <see langword="null"/> when none are found and none are probed.</returns>
    /// <remarks>
    /// A cache that can't be written isn't an error: the results are still returned.
    /// </remarks>
    public static async Task<RuleProbeSet?> LoadOrProbeAsync(
        string spec,
        string featureCatalogueHash,
        string portrayalCatalogueHash,
        string? cacheDirectory,
        Func<CancellationToken, Task<RuleProbeSet>>? probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        ArgumentException.ThrowIfNullOrEmpty(featureCatalogueHash);
        ArgumentException.ThrowIfNullOrEmpty(portrayalCatalogueHash);

        if (await TryLoadBundledAsync(spec, featureCatalogueHash, portrayalCatalogueHash, cancellationToken).ConfigureAwait(false) is { } bundled)
            return bundled;
        if (cacheDirectory is not null && TryLoadCached(cacheDirectory, spec, featureCatalogueHash, portrayalCatalogueHash) is { } cached)
            return cached;
        if (probe is null)
            return null;

        var probed = await probe(cancellationToken).ConfigureAwait(false);
        if (cacheDirectory is not null && probed.Matches(featureCatalogueHash, portrayalCatalogueHash))
        {
            try
            {
                SaveCached(cacheDirectory, probed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Probed again next time.
            }
        }

        return probed;
    }

    private static bool IsCompressed(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    private static void WriteFeatureType(Utf8JsonWriter writer, FeatureTypeProbe probe)
    {
        writer.WriteStartObject();
        writer.WriteString("code", probe.Code);
        writer.WriteBoolean("partial", probe.IsPartial);

        writer.WriteStartArray("inputs");
        foreach (var input in probe.Inputs)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(KindName(input.Kind));
            writer.WriteStringValue(input.Code);
            writer.WriteStartArray();
            foreach (var value in input.Values)
            {
                if (value is null) writer.WriteNullValue();
                else writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("lines");
        foreach (var line in probe.ConditionLines)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(line.File);
            writer.WriteNumberValue(line.Line);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("outcomes");
        foreach (var outcome in probe.Outcomes)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("parts");
            foreach (var part in outcome.Parts) writer.WriteStringValue(part);
            writer.WriteEndArray();
            writer.WriteStartArray("vg");
            foreach (var group in outcome.ViewingGroups) writer.WriteNumberValue(group);
            writer.WriteEndArray();
            writer.WriteStartArray("cond");
            foreach (var condition in outcome.Conditions) writer.WriteNumberValue(condition);
            writer.WriteEndArray();
            if (outcome.IsError) writer.WriteBoolean("error", true);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("variants");
        foreach (var variant in probe.Variants) writer.WriteNumberValue(variant);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static RuleProbeSet ReadSet(JsonElement root)
    {
        var format = root.GetProperty("format").GetInt32();
        if (format != RuleProbeSet.CurrentFormatVersion)
            throw new InvalidDataException($"The probe results are in format {format}; this version reads format {RuleProbeSet.CurrentFormatVersion}.");

        var types = new List<FeatureTypeProbe>();
        foreach (var element in root.GetProperty("featureTypes").EnumerateArray())
        {
            types.Add(ReadFeatureType(element));
        }

        types.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        return new RuleProbeSet
        {
            FormatVersion = format,
            Spec = root.GetProperty("spec").GetString() ?? throw new FormatException("No spec."),
            FeatureCatalogueHash = root.GetProperty("fc").GetString() ?? throw new FormatException("No feature catalogue hash."),
            PortrayalCatalogueHash = root.GetProperty("pc").GetString() ?? throw new FormatException("No portrayal catalogue hash."),
            Cap = root.GetProperty("cap").GetInt32(),
            FeatureTypes = types,
        };
    }

    private static FeatureTypeProbe ReadFeatureType(JsonElement element)
    {
        var inputs = new List<ProbeInput>();
        foreach (var input in element.GetProperty("inputs").EnumerateArray())
        {
            var values = new List<string?>();
            foreach (var value in input[2].EnumerateArray())
            {
                values.Add(value.ValueKind == JsonValueKind.Null ? null : value.GetString());
            }

            inputs.Add(new ProbeInput(ParseKind(input[0].GetString()), input[1].GetString() ?? string.Empty, values));
        }

        var lines = element.GetProperty("lines").EnumerateArray()
            .Select(l => new ProbeConditionLine(l[0].GetString() ?? string.Empty, l[1].GetInt32()))
            .ToList();

        var outcomes = new List<ProbeOutcome>();
        foreach (var outcome in element.GetProperty("outcomes").EnumerateArray())
        {
            outcomes.Add(new ProbeOutcome
            {
                Parts = outcome.GetProperty("parts").EnumerateArray().Select(p => p.GetString() ?? string.Empty).ToList(),
                ViewingGroups = outcome.GetProperty("vg").EnumerateArray().Select(g => g.GetInt32()).ToList(),
                Conditions = outcome.GetProperty("cond").EnumerateArray().Select(c => c.GetInt32()).ToList(),
                IsError = outcome.TryGetProperty("error", out var error) && error.GetBoolean(),
            });
        }

        var variants = element.GetProperty("variants").EnumerateArray().Select(v => v.GetInt32()).ToList();
        var expected = inputs.Aggregate(1L, (n, i) => n * Math.Max(1, i.Values.Count));
        if (variants.Count != expected || variants.Any(v => v < 0 || v >= outcomes.Count))
            throw new FormatException($"The variants of {element.GetProperty("code").GetString()} don't match its inputs.");

        return new FeatureTypeProbe
        {
            Code = element.GetProperty("code").GetString() ?? throw new FormatException("No code."),
            IsPartial = element.GetProperty("partial").GetBoolean(),
            Inputs = inputs,
            ConditionLines = lines,
            Outcomes = outcomes,
            Variants = variants,
        };
    }

    private static string KindName(ProbeInputKind kind) => kind switch
    {
        ProbeInputKind.Primitive => "primitive",
        ProbeInputKind.Attribute => "attribute",
        ProbeInputKind.ContextParameter => "context",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ProbeInputKind ParseKind(string? name) => name switch
    {
        "primitive" => ProbeInputKind.Primitive,
        "attribute" => ProbeInputKind.Attribute,
        "context" => ProbeInputKind.ContextParameter,
        _ => throw new FormatException($"Unknown input kind '{name}'."),
    };
}
