using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A block of pick report content that isn't an attribute row, such as an ice
/// egg code. A section is plain data with a stable <see cref="Kind"/>; each
/// host presents the kinds it knows and shows <see cref="ToRows"/> for the
/// rest.
/// </summary>
/// <remarks>
/// <para>
/// Whatever a section shows that depends on time is in the section itself,
/// so a host never re-runs a pick when the view time changes.
/// </para>
/// <para>
/// Sections reach <see cref="FeatureInfo.Sections"/> from the processor or
/// from an <see cref="IPickContributor"/>. Each kind is registered in a
/// <see cref="PickSectionKindRegistry"/>, which tells MCP how to serialize it.
/// </para>
/// </remarks>
public abstract record PickSection
{
    /// <summary>
    /// The section's kind, a dotted string such as <c>"ice.egg-code"</c>.
    /// Built-in kinds have no owner prefix; other kinds start with their
    /// owner's name (<c>"acme.ice-chart"</c>).
    /// </summary>
    public abstract string Kind { get; }

    /// <summary>
    /// The section as attribute rows, which a host that doesn't know
    /// <see cref="Kind"/> shows instead.
    /// </summary>
    public abstract IReadOnlyList<PickAttribute> ToRows();
}

/// <summary>
/// The WMO / SIGRID-3 ice egg code of a sea-ice or lake-ice feature, from any
/// product or source that carries SIGRID-3 concentration, stage of development
/// and form-of-ice attributes (S-411 Edition 1.2.1 Annex A).
/// </summary>
/// <param name="Egg">The egg code.</param>
public sealed record IceEggCodeSection(IceEggCode Egg) : PickSection
{
    /// <summary>The kind of an <see cref="IceEggCodeSection"/>.</summary>
    public const string KindName = "ice.egg-code";

    /// <inheritdoc />
    [JsonIgnore]
    public override string Kind => KindName;

    /// <inheritdoc />
    public override IReadOnlyList<PickAttribute> ToRows()
    {
        var rows = new List<PickAttribute>(5);
        Add("Ct", "Total concentration", Egg.TotalConcentration is { } ct ? [ct] : []);
        Add("C", "Partial concentrations", [.. Egg.PartialConcentrations, .. Egg.TrailingPartialConcentrations]);
        Add("S", "Stages of development", [.. Egg.StagesOfDevelopment, .. Egg.TrailingStagesOfDevelopment]);
        Add("F", "Forms of ice", [.. Egg.FormsOfIce, .. Egg.TrailingFormsOfIce]);
        Add("Sd", "Snow depth", [.. Egg.Annotations.Where(a => a.Role == IceEggValueRole.SnowDepth)]);
        return rows;

        void Add(string code, string name, IReadOnlyList<IceEggValue> values)
        {
            if (values.Count == 0)
                return;
            var raw = string.Join(" ", values.Select(v => v.Text));
            var definitions = values.Where(v => v.Definition is not null).Select(v => v.Definition!).ToList();
            rows.Add(new PickAttribute
            {
                Code = code,
                Name = name,
                RawValue = raw,
                DisplayValue = definitions.Count == values.Count
                    ? string.Join("; ", definitions)
                    : null,
            });
        }
    }
}

/// <summary>
/// A registered <see cref="PickSection"/> kind: its name, its section type, a
/// one-line description, and the JSON metadata MCP serializes it with.
/// </summary>
/// <param name="Kind">The kind's name, matching <see cref="PickSection.Kind"/>.</param>
/// <param name="TypeInfo">Source-generated JSON metadata for the section type, which also names the type.</param>
/// <param name="Description">One line on what the section holds, for MCP clients.</param>
public sealed record PickSectionKind(string Kind, JsonTypeInfo TypeInfo, string Description)
{
    /// <summary>The section type this kind serializes.</summary>
    public Type SectionType => TypeInfo.Type;

    /// <summary>Serializes <paramref name="section"/> with <see cref="TypeInfo"/>.</summary>
    /// <param name="section">A section of this kind.</param>
    public JsonElement Serialize(PickSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return JsonSerializer.SerializeToElement(section, TypeInfo);
    }
}

/// <summary>
/// The <see cref="PickSection"/> kinds a host knows. MCP serializes a section
/// whose kind is registered; a section of an unknown kind reaches MCP as its
/// rows only.
/// </summary>
/// <remarks>
/// Register kinds at start-up, before the first pick. The process-wide
/// <see cref="Default"/> registry holds the built-in kinds.
/// </remarks>
public sealed class PickSectionKindRegistry
{
    private readonly Dictionary<string, PickSectionKind> _byKind = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>The process-wide registry, pre-loaded with the built-in kinds.</summary>
    public static PickSectionKindRegistry Default { get; } = CreateDefault();

    /// <summary>Creates a registry with the built-in kinds.</summary>
    public static PickSectionKindRegistry CreateDefault()
    {
        var registry = new PickSectionKindRegistry();
        registry.Register(new PickSectionKind(
            IceEggCodeSection.KindName,
            PickSectionJsonContext.Default.IceEggCodeSection,
            "WMO / SIGRID-3 ice egg code: total concentration, then partial concentration, stage of development and form of ice for each ice type, with snow depth."));
        return registry;
    }

    /// <summary>The registered kinds, in registration order.</summary>
    public IReadOnlyList<PickSectionKind> Kinds
    {
        get
        {
            lock (_lock)
                return [.. _byKind.Values];
        }
    }

    /// <summary>Registers a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <exception cref="ArgumentException">The kind's name is empty or already registered.</exception>
    public void Register(PickSectionKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind.Kind);
        lock (_lock)
        {
            if (!_byKind.TryAdd(kind.Kind, kind))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "The pick section kind '{0}' is already registered.", kind.Kind),
                    nameof(kind));
            }
        }
    }

    /// <summary>The registered kind named <paramref name="kind"/>, or <see langword="null"/>.</summary>
    /// <param name="kind">The kind's name.</param>
    public PickSectionKind? Find(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        lock (_lock)
            return _byKind.GetValueOrDefault(kind);
    }
}

/// <summary>Source-generated JSON metadata for the built-in pick sections.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IceEggCodeSection))]
internal sealed partial class PickSectionJsonContext : JsonSerializerContext;
