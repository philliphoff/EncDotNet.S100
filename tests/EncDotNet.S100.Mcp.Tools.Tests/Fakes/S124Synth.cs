using System.Collections.ObjectModel;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S124Synth
{
    public static S124Dataset Dataset(params S124Feature[] features) => new()
    {
        Features = features.ToArray(),
        InformationTypes = [],
    };

    public static S124Dataset Dataset(IEnumerable<S124Feature> features, IEnumerable<S124InformationType> infos) => new()
    {
        Features = features.ToArray(),
        InformationTypes = infos.ToArray(),
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
            GeometryType = S100GeometryType.Point,
            Attributes = (attributes ?? new Dictionary<string, string>()).ToDictionary(),
            ComplexAttributes = (complex ?? []).ToArray(),
            References = (references ?? []).ToArray(),
        };
    }

    public static S124InformationType Info(string id, string typeCode = "NavwarnPreamble") => new()
    {
        Id = id,
        TypeCode = typeCode,
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    public static GmlReference Ref(string role, string href) => new()
    {
        Role = role,
        Href = href,
    };

    public static S124ComplexAttribute Complex(string code, IDictionary<string, string> sub) => new()
    {
        Code = code,
        SubAttributes = sub.ToDictionary(),
    };
}
