using System.Collections.Immutable;
using System.IO;
using System.Reflection;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Code mapping from S-57 numeric object/attribute codes to S-101 Feature
/// Catalogue acronyms. Loaded from embedded CSV resources curated against
/// IHO's S-57 Edition 3.1 Appendix A and the S-57 → S-101 conversion
/// specification.
/// </summary>
public sealed class S57S101Mapping
{
    private readonly ImmutableDictionary<ushort, string> _featureMap;
    private readonly ImmutableDictionary<ushort, string> _attributeMap;

    private S57S101Mapping(
        ImmutableDictionary<ushort, string> featureMap,
        ImmutableDictionary<ushort, string> attributeMap)
    {
        _featureMap = featureMap;
        _attributeMap = attributeMap;
    }

    /// <summary>
    /// Returns the default mapping bundled with this assembly. Suitable for
    /// most ENC base cells; extend by composing your own <see cref="Builder"/>
    /// when a producer uses additional object classes.
    /// </summary>
    public static S57S101Mapping Default { get; } = LoadFromResources();

    /// <summary>
    /// Resolves a numeric S-57 OBJL code to its S-101 Feature Catalogue code.
    /// Returns <c>null</c> when the code is not in the mapping table.
    /// </summary>
    public string? ResolveFeatureCode(ushort objl)
        => _featureMap.TryGetValue(objl, out var code) ? code : null;

    /// <summary>
    /// Resolves a numeric S-57 ATTL code to its S-101 Feature Catalogue code.
    /// Returns <c>null</c> when the code is not in the mapping table.
    /// </summary>
    public string? ResolveAttributeCode(ushort attl)
        => _attributeMap.TryGetValue(attl, out var code) ? code : null;

    /// <summary>Number of feature classes in the mapping.</summary>
    public int FeatureCount => _featureMap.Count;

    /// <summary>Number of attributes in the mapping.</summary>
    public int AttributeCount => _attributeMap.Count;

    /// <summary>Builder for composing custom mapping tables.</summary>
    public sealed class Builder
    {
        private readonly ImmutableDictionary<ushort, string>.Builder _features
            = ImmutableDictionary.CreateBuilder<ushort, string>();
        private readonly ImmutableDictionary<ushort, string>.Builder _attributes
            = ImmutableDictionary.CreateBuilder<ushort, string>();

        /// <summary>Adds or replaces a feature class mapping.</summary>
        public Builder AddFeature(ushort objl, string s101Code)
        {
            ArgumentException.ThrowIfNullOrEmpty(s101Code);
            _features[objl] = s101Code;
            return this;
        }

        /// <summary>Adds or replaces an attribute mapping.</summary>
        public Builder AddAttribute(ushort attl, string s101Code)
        {
            ArgumentException.ThrowIfNullOrEmpty(s101Code);
            _attributes[attl] = s101Code;
            return this;
        }

        /// <summary>Pre-populates the builder with the default mapping.</summary>
        public Builder WithDefaults()
        {
            foreach (var kv in Default._featureMap) _features[kv.Key] = kv.Value;
            foreach (var kv in Default._attributeMap) _attributes[kv.Key] = kv.Value;
            return this;
        }

        /// <summary>Builds an immutable mapping.</summary>
        public S57S101Mapping Build()
            => new(_features.ToImmutable(), _attributes.ToImmutable());
    }

    // ── Loading ─────────────────────────────────────────────────────────

    private static S57S101Mapping LoadFromResources()
    {
        var asm = typeof(S57S101Mapping).Assembly;
        var features = LoadCsv(asm, "EncDotNet.S100.Datasets.S57.Mapping.s57-objl-to-s101.csv");
        var attributes = LoadCsv(asm, "EncDotNet.S100.Datasets.S57.Mapping.s57-attl-to-s101.csv");
        return new S57S101Mapping(features, attributes);
    }

    private static ImmutableDictionary<ushort, string> LoadCsv(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        var builder = ImmutableDictionary.CreateBuilder<ushort, string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var parts = trimmed.Split(',');
            if (parts.Length < 3) continue;
            if (!ushort.TryParse(parts[0], out var code)) continue;

            var s101 = parts[2].Trim();
            if (s101.Length == 0) continue;
            builder[code] = s101;
        }
        return builder.ToImmutable();
    }
}
