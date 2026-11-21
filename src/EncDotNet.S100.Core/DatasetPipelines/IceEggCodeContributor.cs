using System.Collections.Immutable;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Adds the WMO / SIGRID-3 ice egg code (<see cref="IceEggCodeSection"/>) to
/// any feature that carries egg code attributes, whichever product or source
/// it comes from.
/// </summary>
/// <remarks>
/// <para>
/// The attributes are read by their S-411 codes: the JCOMM short codes
/// <c>iceact</c>, <c>iceapc</c>, <c>icesod</c> and <c>iceflz</c>, the
/// canonical <c>totalConcentration</c>, and <c>snowDepth</c> in centimetres.
/// </para>
/// <para>
/// A feature gets an egg code when it's an S-411 sea-ice or lake-ice feature
/// (<c>seaice</c>, <c>SeaIce</c>, <c>lacice</c>, <c>LakeIce</c>), or when it
/// carries a total or partial concentration. S-411 ice leads and fractures,
/// which carry a stage of development alone, don't. When the feature's
/// catalogue defines the values, each value carries its definition
/// (S-411 Edition 1.2.1 Annex A).
/// </para>
/// </remarks>
public sealed class IceEggCodeContributor : IPickContributor
{
    /// <summary>The shared instance.</summary>
    public static IceEggCodeContributor Instance { get; } = new();

    private static readonly HashSet<string> IceAreaFeatureTypes =
        new(StringComparer.OrdinalIgnoreCase) { "seaice", "lacice", "LakeIce" };

    /// <summary>
    /// JCOMM short and canonical attribute codes (as carried on
    /// <see cref="IceEggValue.SourceCode"/>) mapped to their S-411 feature
    /// catalogue codes, which the definitions are resolved against.
    /// </summary>
    private static readonly Dictionary<string, string> CatalogueCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["iceact"] = "totalConcentration",
            ["totalConcentration"] = "totalConcentration",
            ["iceapc"] = "partialConcentration",
            ["icesod"] = "iceStageofDevelopment",
            ["iceflz"] = "floeSizes",
        };

    /// <inheritdoc />
    public PickSection? Contribute(PickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Skip an empty JCOMM iceact so the canonical totalConcentration can
        // fill Ct; RawValue already ignores blank values.
        string? totalConcentration = null;
        var totalConcentrationSourceCode = "iceact";
        foreach (var code in new[] { "iceact", "totalConcentration" })
        {
            if (context.RawValue(code) is { } value)
            {
                totalConcentration = value;
                totalConcentrationSourceCode = code;
                break;
            }
        }

        var partialConcentrations = context.RawValue("iceapc");
        if (!IceAreaFeatureTypes.Contains(context.FeatureType)
            && totalConcentration is null
            && partialConcentrations is null)
        {
            return null;
        }

        double? snowDepth = null;
        if (context.RawValue("snowDepth") is { } snowRaw
            && double.TryParse(snowRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var snow))
        {
            snowDepth = snow;
        }

        var egg = IceEggCodeBuilder.Build(
            totalConcentration,
            partialConcentrations,
            context.RawValue("icesod"),
            context.RawValue("iceflz"),
            snowDepth,
            totalConcentrationSourceCode);
        if (egg is null)
            return null;

        return new IceEggCodeSection(
            context.ResolveListedValueDefinition is { } resolve ? WithDefinitions(egg, resolve) : egg);
    }

    private static IceEggCode WithDefinitions(IceEggCode egg, Func<string, string, string?> resolve)
    {
        IceEggValue? Define(IceEggValue? value)
        {
            if (value is null || value.SourceCode is not { } source
                || !CatalogueCodes.TryGetValue(source, out var catalogueCode))
                return value;
            var definition = resolve(catalogueCode, value.Text);
            return definition is null ? value : value with { Definition = definition };
        }

        ImmutableArray<IceEggValue> DefineRow(ImmutableArray<IceEggValue> row)
        {
            if (row.IsDefaultOrEmpty)
                return row;
            var builder = ImmutableArray.CreateBuilder<IceEggValue>(row.Length);
            foreach (var value in row)
                builder.Add(Define(value)!);
            return builder.ToImmutable();
        }

        return egg with
        {
            TotalConcentration = Define(egg.TotalConcentration),
            PartialConcentrations = DefineRow(egg.PartialConcentrations),
            StagesOfDevelopment = DefineRow(egg.StagesOfDevelopment),
            FormsOfIce = DefineRow(egg.FormsOfIce),
            TrailingPartialConcentrations = DefineRow(egg.TrailingPartialConcentrations),
            TrailingStagesOfDevelopment = DefineRow(egg.TrailingStagesOfDevelopment),
            TrailingFormsOfIce = DefineRow(egg.TrailingFormsOfIce),
            Annotations = DefineRow(egg.Annotations),
        };
    }
}
