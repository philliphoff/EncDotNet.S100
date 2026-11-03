using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests.RoundTrip;

/// <summary>One field that differs between an S-57 model and the same model written and read back.</summary>
/// <param name="Field">The record and field, for example <c>DSID.IssueDate</c> or <c>feature.ATTF</c>.</param>
/// <param name="Detail">The record key and both values.</param>
internal sealed record S57FieldDifference(string Field, string Detail);

/// <summary>
/// Compares two S-57 models field by field: the translator's output and what
/// <see cref="S57DocumentReader"/> reads back after <see cref="S57DocumentWriter"/>
/// wrote it. Vector records match by RCNM/RCID, feature records by RCID.
/// </summary>
internal static class S57ModelDiff
{
    public static List<S57FieldDifference> Compare(S57Document expected, S57Document actual)
    {
        var differences = new List<S57FieldDifference>();
        Fields("DSID", "", Properties(expected.DataSetIdentification), Properties(actual.DataSetIdentification), differences);
        Fields("DSPM", "", Properties(expected.DataSetParameters), Properties(actual.DataSetParameters), differences);
        Records("vector", expected.VectorRecords, actual.VectorRecords,
            v => $"{v.RecordName.RecordNameCode}/{v.RecordName.RecordId}", Vector, differences);
        Records("feature", expected.FeatureRecords, actual.FeatureRecords,
            f => f.RecordName.RecordId.ToString(CultureInfo.InvariantCulture), Feature, differences);
        return differences;
    }

    private static IEnumerable<(string Field, string Value)> Vector(S57VectorRecord v)
    {
        yield return ("VRID", $"{v.RecordVersion} {v.UpdateInstruction}");
        yield return ("ATTV", Join(v.Attributes));
        yield return ("VRPT", Join(v.VectorPointers));
        yield return ("SG2D", Join(v.Coordinates2D));
        yield return ("SG3D", Join(v.Soundings));
    }

    private static IEnumerable<(string Field, string Value)> Feature(S57FeatureRecord f)
    {
        yield return ("FRID", $"{f.Primitive} {f.Group} {(int)f.ObjectCode} {f.RecordVersion} {f.UpdateInstruction}");
        yield return ("FOID", $"{f.RecordName.AgencyCode} {unchecked((uint)f.RecordName.FeatureId)} {f.RecordName.FeatureSubdivision}");
        yield return ("ATTF", Join(f.Attributes));
        yield return ("NATF", Join(f.NationalAttributes));
        yield return ("FFPT", Join(f.FeaturePointers));
        yield return ("FSPT", Join(f.SpatialPointers));
    }

    private static void Records<T>(
        string kind,
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        Func<T, string> key,
        Func<T, IEnumerable<(string Field, string Value)>> fields,
        List<S57FieldDifference> differences)
    {
        var actualByKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var record in actual)
            actualByKey.TryAdd(key(record), record);

        foreach (var record in expected)
        {
            var k = key(record);
            if (!actualByKey.Remove(k, out var other))
            {
                differences.Add(new($"{kind}.(missing)", k));
                continue;
            }
            Fields(kind, k, fields(record), fields(other), differences);
        }
        foreach (var k in actualByKey.Keys)
            differences.Add(new($"{kind}.(added)", k));
    }

    private static void Fields(
        string kind,
        string key,
        IEnumerable<(string Field, string Value)> expected,
        IEnumerable<(string Field, string Value)> actual,
        List<S57FieldDifference> differences)
    {
        var actualByField = actual.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal);
        foreach (var (field, value) in expected)
        {
            var other = actualByField.GetValueOrDefault(field);
            if (!string.Equals(value, other, StringComparison.Ordinal))
                differences.Add(new($"{kind}.{field}", $"{key}: {value} → {other}".TrimStart(':', ' ')));
        }
    }

    private static IEnumerable<(string Field, string Value)> Properties<T>(T? value) where T : class
        => typeof(T).GetProperties()
            .Select(p => (p.Name, value is null ? "∅" : Convert.ToString(p.GetValue(value), CultureInfo.InvariantCulture) ?? ""));

    private static string Join<T>(IEnumerable<T> items) => string.Join("; ", items);
}
