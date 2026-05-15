using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S124Synth
{
    public static S124Dataset Dataset(params S124Feature[] features) => new()
    {
        Features = features.ToImmutableArray(),
        InformationTypes = ImmutableArray<S124InformationType>.Empty,
    };

    public static S124Dataset Dataset(IEnumerable<S124Feature> features, IEnumerable<S124InformationType> infos) => new()
    {
        Features = features.ToImmutableArray(),
        InformationTypes = infos.ToImmutableArray(),
    };

    public static S124Feature Feature(
        string id,
        string featureType = "NavwarnPart",
        IDictionary<string, string>? attributes = null,
        IEnumerable<S124ComplexAttribute>? complex = null,
        IEnumerable<GmlReference>? references = null)
    {
        return new S124Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = GmlGeometryType.Point,
            Attributes = (attributes ?? new Dictionary<string, string>()).ToImmutableDictionary(),
            ComplexAttributes = (complex ?? []).ToImmutableArray(),
            References = (references ?? []).ToImmutableArray(),
        };
    }

    public static S124InformationType Info(string id, string typeCode = "NavwarnPreamble") => new()
    {
        Id = id,
        TypeCode = typeCode,
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
    };

    public static GmlReference Ref(string role, string href) => new()
    {
        Role = role,
        Href = href,
    };

    public static S124ComplexAttribute Complex(string code, IDictionary<string, string> sub) => new()
    {
        Code = code,
        SubAttributes = sub.ToImmutableDictionary(),
    };
}
